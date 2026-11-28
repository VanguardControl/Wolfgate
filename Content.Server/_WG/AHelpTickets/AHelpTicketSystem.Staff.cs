using System.Linq;
using Content.Server._WG.ShipAdmin;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.AHelpTickets;
using Content.Shared._WG.Companies;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Chat;
using Content.Shared.Clothing;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles.Jobs;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WG.AHelpTickets;

// What staff do around a ticket besides answering it: who is looking at it, asking others for help, the player's
// ships and handing back their loadout.
public sealed partial class AHelpTicketSystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private WGShipAdminSystem _shipAdmin = default!;


    /// <summary>How long a ticket waits before the same request for help can go out again.</summary>
    private static readonly TimeSpan AssistCooldown = TimeSpan.FromSeconds(30);

    /// <summary>The ticket each staffer has open in their panel.</summary>
    private readonly Dictionary<ICommonSession, int> _viewing = new();

    private readonly Dictionary<int, TimeSpan> _lastAssist = new();

    private void InitializeStaff()
    {
        SubscribeNetworkEvent<AHelpTicketViewMessage>(OnView);
        SubscribeNetworkEvent<AHelpTicketAssistMessage>(OnAssist);
        SubscribeNetworkEvent<AHelpTicketLoadoutMessage>(OnLoadout);
    }

    #region Viewers

    private void OnView(AHelpTicketViewMessage msg, EntitySessionEventArgs args)
    {
        var staff = args.SenderSession;
        if (msg.Key is { } key && (!_tickets.TryGetValue(key, out var ticket) || !_bwoink.WGCanAnswer(staff, ticket.Info.Player)))
            return;

        SetViewing(staff, msg.Key);
    }

    /// <summary>Moves a staffer's view to a ticket, or off every ticket, and tells staff whose list it changes.</summary>
    private void SetViewing(ICommonSession staff, int? key)
    {
        var old = _viewing.TryGetValue(staff, out var previous) ? previous : (int?) null;
        if (old == key)
            return;

        if (key is { } now)
            _viewing[staff] = now;
        else
            _viewing.Remove(staff);

        foreach (var changed in new[] { old, key })
        {
            if (changed is { } changedKey && _tickets.TryGetValue(changedKey, out var ticket))
            {
                ticket.Info.Viewers = Viewers(changedKey);
                SendUpdate(ticket);
            }
        }
    }

    private List<string> Viewers(int key)
    {
        return _viewing.Where(v => v.Value == key).Select(v => v.Key.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    #endregion

    #region Assistance

    /// <summary>Pings the other staff who see the ticket, with a sound of its own and a link that opens it.</summary>
    private void OnAssist(AHelpTicketAssistMessage msg, EntitySessionEventArgs args)
    {
        var staff = args.SenderSession;
        if (!_tickets.TryGetValue(msg.Key, out var ticket) || ticket.Retired || !_bwoink.WGCanAnswer(staff, ticket.Info.Player))
            return;

        if (_lastAssist.TryGetValue(msg.Key, out var last) && _timing.RealTime - last < AssistCooldown)
        {
            var wait = (int) Math.Ceiling((AssistCooldown - (_timing.RealTime - last)).TotalSeconds);
            _chat.DispatchServerMessage(staff, Loc.GetString("wg-ahelp-assist-cooldown", ("seconds", wait)));
            return;
        }

        var info = ticket.Info;
        var others = _bwoink.WGTargetAdmins(info.Category).Where(c => c != staff.Channel).ToList();
        if (others.Count == 0)
        {
            _chat.DispatchServerMessage(staff, Loc.GetString("wg-ahelp-assist-nobody"));
            return;
        }

        _lastAssist[msg.Key] = _timing.RealTime;
        var name = info.Character.Length > 0 ? $"{info.Character} ({info.Username})" : info.Username;
        var text = Loc.GetString("wg-ahelp-assist-chat",
            ("staff", staff.Name),
            ("number", info.Number),
            ("player", name));
        var wrapped = Loc.GetString("wg-ahelp-assist-chat-wrapped",
            ("text", FormattedMessage.EscapeText(text)),
            ("key", info.Key.ToString()));

        // The sound comes on its own, so staff who hide admin chat still hear it; the chat line carries the link.
        _chat.ChatMessageToMany(ChatChannel.AdminChat, text, wrapped, EntityUid.Invalid, false, true, others);
        var alert = new AHelpTicketAssistAlertMessage(info.Key, _cfg.GetCVar(AHelpTicketCVars.AssistSound));
        foreach (var channel in others)
        {
            RaiseNetworkEvent(alert, channel);
        }

        _bwoink.WGStaffLine(info.Player, Loc.GetString("wg-ahelp-assist-line", ("staff", FormattedMessage.EscapeText(staff.Name))), playSound: false);
    }

    #endregion

    #region Ships

    /// <summary>The ships the player owns: by the shipyard's record, ship access or a deed card issued to them.</summary>
    private List<AHelpTicketShip> OwnedShips(NetUserId player)
    {
        var ships = new List<AHelpTicketShip>();
        var grids = AllEntityQuery<MapGridComponent, MetaDataComponent>();
        while (grids.MoveNext(out var grid, out _, out var meta))
        {
            if (!HasComp<ShuttleDeedComponent>(grid) && !HasComp<ShipOwnershipComponent>(grid) && !HasComp<WGShipAccessComponent>(grid))
                continue;

            if (_shipAdmin.OwnerAccounts(grid).Contains(player))
                ships.Add(new AHelpTicketShip(GetNetEntity(grid), meta.EntityName));
        }

        return ships.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    #endregion

    #region Loadout

    /// <summary>The loadouts the player picked for the job they are playing, and the character they picked them on.</summary>
    private bool TryGetLoadout(ICommonSession session, out EntityUid body, out List<LoadoutPrototype> loadouts)
    {
        body = default;
        loadouts = new List<LoadoutPrototype>();
        if (session.AttachedEntity is not { } attached
            || !_minds.TryGetMind(session.UserId, out var mind)
            || !_jobs.MindTryGetJobId(mind.Value.Owner, out var job) || job == null
            || _preferences.GetPreferencesOrNull(session.UserId) is not { } prefs)
            return false;

        body = attached;
        var character = Name(attached);
        // No match means no loadout: the selected slot may be another character with the same job.
        var profile = prefs.Characters.Values.OfType<HumanoidCharacterProfile>().FirstOrDefault(p => p.Name == character);
        if (profile == null || !profile.Loadouts.TryGetValue(LoadoutSystem.GetJobPrototype(job.Value), out var role))
            return false;

        foreach (var selected in role.SelectedLoadouts.Values.SelectMany(l => l))
        {
            if (_prototypes.TryIndex(selected.Prototype, out var loadout))
                loadouts.Add(loadout);
        }

        return loadouts.Count > 0;
    }

    /// <summary>What the player's loadout costs, or null when there is none to give.</summary>
    private int? LoadoutPrice(NetUserId player)
    {
        if (!_players.TryGetSessionById(player, out var session) || !TryGetLoadout(session, out _, out var loadouts))
            return null;

        return loadouts.Sum(l => Math.Max(0, l.Price));
    }

    /// <summary>
    /// Gives the player their job loadout again, into their bag or hands. Charged, the whole price comes out of their
    /// bank first, or nothing is given. The result goes in the ticket for staff.
    /// </summary>
    private void OnLoadout(AHelpTicketLoadoutMessage msg, EntitySessionEventArgs args)
    {
        var staff = args.SenderSession;
        if (!_admins.HasAdminFlag(staff, Content.Shared.Administration.AdminFlags.Admin) || !_bwoink.WGCanAnswer(staff, msg.Player))
            return;

        if (!_players.TryGetSessionById(msg.Player, out var session) || session.Status != SessionStatus.InGame
            || !TryGetLoadout(session, out var body, out var loadouts))
        {
            _bwoink.WGStaffLine(msg.Player, Loc.GetString("wg-ahelp-loadout-none"), playSound: false);
            return;
        }

        var price = loadouts.Sum(l => Math.Max(0, l.Price));
        if (msg.Charge && price > 0 && !_bank.TryBankWithdraw(body, price))
        {
            _bwoink.WGStaffLine(msg.Player, Loc.GetString("wg-ahelp-loadout-broke", ("price", price)), playSound: false);
            return;
        }

        var coordinates = Transform(body).Coordinates;
        var count = 0;
        foreach (var loadout in loadouts)
        {
            foreach (var proto in WGCompanyLoadouts.Items(_prototypes, loadout))
            {
                var item = Spawn(proto, coordinates);
                count++;
                if (_inventory.TryGetSlotEntity(body, "back", out var bag) && _storage.Insert(bag.Value, item, out _, playSound: false))
                    continue;

                _hands.PickupOrDrop(body, item, checkActionBlocker: false, animate: false);
            }
        }

        var charged = msg.Charge ? price : 0;
        _adminLog.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{ToPrettyString(staff.AttachedEntity):staff} gave {ToPrettyString(body):player} their loadout ({count} items) from the ahelp ticket panel, charging {charged}");
        _bwoink.WGStaffLine(msg.Player, Loc.GetString("wg-ahelp-loadout-given",
            ("staff", FormattedMessage.EscapeText(staff.Name)),
            ("count", count),
            ("charged", charged)), playSound: false);

        // The player hears of it too, above all when it cost them.
        _chat.DispatchServerMessage(session, Loc.GetString("wg-ahelp-loadout-player", ("count", count), ("charged", charged)));
    }

    #endregion
}
