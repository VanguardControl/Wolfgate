using System.Linq;
using Content.Server._NF.Bank;
using Content.Server.Chat.Managers;
using Content.Server._WG.Companies;
using Content.Shared._Mono.Company;
using Content.Shared._WG.Companies;
using Content.Shared._WG.StockExchange;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Materials;
using Content.Server.Stack;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.StockExchange;

/// <summary>
/// Grid exchanges with real order books for materials, and faction or company accounts that hold their listings and
/// stock. A character in a gameplay faction always acts for the faction, never for their company.
/// </summary>
public sealed partial class WGStockSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Content.Server.Preferences.Managers.IServerPreferencesManager _preferences = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WGCompanySystem _companies = default!;
    [Dependency] private IChatManager _chat = default!;

    private readonly Dictionary<string, EntityUid> _accounts = new();

    private sealed record OwnerInfo(EntityUid Account, WGStockAccountComponent AccountComp, WGStockOwnerKind Kind,
        int CompanyId, string Name, string Tag, long Balance, bool Frozen, WGCompanyPermissions Permissions, int? Profile,
        string ActorName);

    public override void Initialize()
    {
        base.Initialize();
        InitializeUi();
        InitializeInteraction();
        InitializeRemote();
        InitializeSellConsole();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        foreach (var uid in _accounts.Values)
        {
            if (!TerminatingOrDeleted(uid))
                QueueDel(uid);
        }
        _accounts.Clear();

        var markets = EntityQueryEnumerator<WGStockMarketComponent>();
        while (markets.MoveNext(out var uid, out _))
            RemComp<WGStockMarketComponent>(uid);
    }

    /// <summary>The account the acting body trades for: its faction if it has one, otherwise its company.</summary>
    private OwnerInfo? ResolveOwner(EntityUid actor, out string? error)
    {
        error = null;
        if (!_players.TryGetSessionByEntity(actor, out var session))
        {
            error = "wg-stock-error-no-character";
            return null;
        }

        if (TryComp<CompanyComponent>(actor, out var faction) && WGStockPolicy.FactionTakesPrecedence(faction.CompanyName))
        {
            var factionId = faction.CompanyName.Id;
            var name = _protos.TryIndex<CompanyPrototype>(factionId, out var prototype) ? prototype.Name : factionId;
            var account = GetFactionAccount(factionId, name);
            var accountComp = Comp<WGStockAccountComponent>(account);
            var character = _companies.CharacterFor(session);
            return new OwnerInfo(account, accountComp, WGStockOwnerKind.Faction, 0, name, factionId,
                accountComp.FactionBalance, false, WGCompanyPermissions.All, character?.ProfileId, character?.Name ?? string.Empty);
        }

        if (_companies.StockCompany(session) is not { } company)
        {
            error = "wg-stock-error-no-owner";
            return null;
        }

        var companyAccount = GetCompanyAccount(company);
        var owner = new OwnerInfo(companyAccount, Comp<WGStockAccountComponent>(companyAccount), WGStockOwnerKind.Company,
            company.Id, company.Name, company.Tag, company.Balance, company.Frozen, company.Permissions, company.Profile,
            company.CharacterName);
        RefreshPersistent(owner, company);
        return owner;
    }

    /// <summary>Tells the account's faction or company that an order traded.</summary>
    private void NotifyTrade(EntityUid accountUid, bool bought, WGStockGood good, int quantity, int price)
    {
        var account = Comp<WGStockAccountComponent>(accountUid);
        var total = (long) quantity * price;
        var message = Loc.GetString(bought ? "wg-stock-notify-buy" : "wg-stock-notify-sell",
            ("owner", account.Name), ("good", GoodName(good)), ("quantity", quantity), ("price", price), ("total", total));
        if (account.Kind == WGStockOwnerKind.Company)
        {
            _companies.StockNotify(account.CompanyId, message);
            return;
        }

        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } body ||
                !TryComp<CompanyComponent>(body, out var faction) || faction.CompanyName.Id != account.Faction)
                continue;
            _chat.DispatchServerMessage(session, message);
        }
    }

    /// <summary>An owner view for an account alone, used when a resting order trades while its holder is offline.</summary>
    private OwnerInfo OwnerFromAccount(EntityUid uid, WGStockAccountComponent account)
    {
        var balance = account.Kind == WGStockOwnerKind.Company
            ? _companies.GetBalance(account.CompanyId) ?? 0
            : account.FactionBalance;
        return new OwnerInfo(uid, account, account.Kind, account.CompanyId, account.Name, account.Tag, balance, false,
            WGCompanyPermissions.All, null, account.Name);
    }

    private EntityUid GetFactionAccount(string faction, string name)
    {
        var key = $"f:{faction}";
        if (_accounts.TryGetValue(key, out var existing) && !TerminatingOrDeleted(existing))
            return existing;

        var uid = Spawn(null, MapCoordinates.Nullspace);
        var account = AddComp<WGStockAccountComponent>(uid);
        account.Kind = WGStockOwnerKind.Faction;
        account.Faction = faction;
        account.Name = name;
        account.Tag = faction;
        _accounts[key] = uid;
        return uid;
    }

    private EntityUid GetCompanyAccount(WGCompanyStockInfo company)
    {
        var key = $"c:{company.Id}";
        if (_accounts.TryGetValue(key, out var existing) && !TerminatingOrDeleted(existing))
            return existing;

        var uid = Spawn(null, MapCoordinates.Nullspace);
        var account = AddComp<WGStockAccountComponent>(uid);
        account.Kind = WGStockOwnerKind.Company;
        account.CompanyId = company.Id;
        account.Name = company.Name;
        account.Tag = company.Tag;
        account.PersistentCapacity = company.SiloSlots;
        foreach (var (material, sheets) in company.SiloMaterials)
            account.PersistentMaterials[material] = sheets;
        _accounts[key] = uid;
        return uid;
    }

    /// <summary>The market on the terminal's grid, created the first time a terminal there is used.</summary>
    private Entity<WGStockMarketComponent> EnsureMarket(EntityUid terminal, WGStockTerminalComponent? component = null)
    {
        var xform = Transform(terminal);
        var grid = xform.GridUid ?? xform.MapUid ?? terminal;
        var market = EnsureComp<WGStockMarketComponent>(grid);
        if (component != null && _protos.HasIndex<WGStockMarketPrototype>(component.Profile))
            market.Profile = component.Profile;
        return (grid, market);
    }

    /// <summary>A good is tradable when the market's profile lists it.</summary>
    private bool IsListed(WGStockMarketComponent market, WGStockGood good)
    {
        if (!_protos.TryIndex(market.Profile, out var profile))
            return false;
        return profile.Materials.Any(material => material.Id == good.Id);
    }

    /// <summary>The price a good opens at, from its prototype's value.</summary>
    private int BasePrice(WGStockGood good)
    {
        if (!_protos.TryIndex<MaterialPrototype>(good.Id, out var material))
            return WGStockPolicy.Minimum;
        return Math.Max(WGStockPolicy.Minimum, (int) Math.Ceiling(material.Price * WGStockPolicy.UnitsPerSheet));
    }

    private string GoodName(WGStockGood good) =>
        _protos.TryIndex<MaterialPrototype>(good.Id, out var material) ? Loc.GetString(material.Name) : good.Id;

}
