using Content.Server.Administration.Managers;
using Content.Shared._WG.Wilderness;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Verbs;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WG.Wilderness;

/// <summary>Staff marking and clearing people and ships by hand: Admin verbs, and <see cref="WGZoneCommand"/>.</summary>
public sealed partial class WGZoneSystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>The flag staff need to mark and clear.</summary>
    public const AdminFlags StaffFlag = AdminFlags.Admin;

    private static readonly SpriteSpecifier SkullIcon = new SpriteSpecifier.Rsi(new("/Textures/_WG/Wilderness/zones.rsi"), "marked");

    private void InitializeAdmin()
    {
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor) || !_admin.HasAdminFlag(actor.PlayerSession, StaffFlag))
            return;

        var admin = actor.PlayerSession;
        if (CanCarry(args.Target))
            AddVerbs(args, admin, args.Target, false);

        if ((IsShip(args.Target) ? args.Target : ShipOf(args.Target)) is { } ship)
            AddVerbs(args, admin, ship, true);
    }

    /// <summary>A mark or clear verb for a person or ship, whichever it needs.</summary>
    private void AddVerbs(GetVerbsEvent<Verb> args, ICommonSession admin, EntityUid carrier, bool ship)
    {
        var marked = HasComp<WGSkullComponent>(carrier);
        var key = (marked ? "wg-zone-verb-clear" : "wg-zone-verb-mark") + (ship ? "-ship" : string.Empty);
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(key),
            Message = Loc.GetString(key + "-description", ("carrier", Name(carrier))),
            Category = VerbCategory.Admin,
            Icon = SkullIcon,
            Act = () =>
            {
                var result = marked ? Loc.GetString("cmd-wg_zone-cleared", ("carrier", ClearList(carrier))) : StaffMarkText(carrier);
                _popup.PopupCursor(result, admin, PopupType.Medium);
            },
            Impact = LogImpact.Medium,
        });
    }

    /// <summary>Whether a person can carry a mark of their own: a living body, not a ghost or a grid.</summary>
    public bool CanCarry(EntityUid uid)
    {
        return HasComp<MobStateComponent>(uid) && !HasComp<GhostComponent>(uid) && !HasComp<MapGridComponent>(uid);
    }

    /// <summary>Marks a ship or person for staff, and tells them.</summary>
    public void StaffMark(EntityUid carrier)
    {
        Mark(carrier);
        TellAboard(carrier, "wg-zone-skull-staff-marked");
    }

    /// <summary>
    /// Clears the mark on a ship or person for staff, and on whoever shares it: the ship's pilots, or the ship a pilot
    /// flies. Either would pass it straight back otherwise. Returns everyone cleared.
    /// </summary>
    public List<EntityUid> StaffClear(EntityUid carrier)
    {
        var cleared = new List<EntityUid> { carrier };
        var pilots = EntityQueryEnumerator<PilotComponent>();
        while (pilots.MoveNext(out var pilot, out var comp))
        {
            if (comp.Console is not { } console || TerminatingOrDeleted(console) || Transform(console).GridUid is not { } ship)
                continue;

            if (ship == carrier && pilot != carrier)
                cleared.Add(pilot);
            else if (pilot == carrier && ship != carrier && IsShip(ship))
                cleared.Add(ship);
        }

        foreach (var uid in cleared)
        {
            RemComp<WGWildernessDwellComponent>(uid);
            if (RemComp<WGSkullComponent>(uid))
                TellAboard(uid, "wg-zone-skull-staff-cleared");
        }

        return cleared;
    }

    /// <summary>Clears a mark and names everyone it came off, for the admin's result line.</summary>
    private string ClearList(EntityUid carrier)
    {
        var names = new List<string>();
        foreach (var uid in StaffClear(carrier))
            names.Add(ToPrettyString(uid).ToString());

        return string.Join(", ", names);
    }

    private string StaffMarkText(EntityUid carrier)
    {
        StaffMark(carrier);
        return Loc.GetString("cmd-wg_zone-marked", ("carrier", ToPrettyString(carrier).ToString()));
    }
}
