using Content.Server.Administration.Managers;
using Content.Shared._WG.Wilderness;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Verbs;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WG.Wilderness;

/// <summary>
/// Staff marking and clearing people and ships by hand: Admin verbs, and <see cref="WGZoneCommand"/>. Staff alone can
/// set a standing mark, which ends only when they clear it, the person dies or the ship is destroyed.
/// </summary>
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
        SubscribeLocalEvent<WGSkullComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    /// <summary>A standing mark ends when its person dies.</summary>
    private void OnMobStateChanged(Entity<WGSkullComponent> ent, ref MobStateChangedEvent args)
    {
        if (!ent.Comp.Staff || args.NewMobState != MobState.Dead)
            return;

        RemComp<WGSkullComponent>(ent);
        TellAboard(ent, "wg-zone-skull-staff-ended");
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

    /// <summary>The mark, standing mark and clear verbs a person or ship has a use for.</summary>
    private void AddVerbs(GetVerbsEvent<Verb> args, ICommonSession admin, EntityUid carrier, bool ship)
    {
        var skull = CompOrNull<WGSkullComponent>(carrier);
        var suffix = ship ? "-ship" : string.Empty;

        if (skull == null)
            AddVerb(args, admin, carrier, "wg-zone-verb-mark" + suffix, () => StaffMarkText(carrier, false));

        if (skull is not { Staff: true })
            AddVerb(args, admin, carrier, "wg-zone-verb-standing" + suffix, () => StaffMarkText(carrier, true));

        if (skull != null)
            AddVerb(args, admin, carrier, "wg-zone-verb-clear" + suffix, () => StaffClearText(carrier));
    }

    private void AddVerb(GetVerbsEvent<Verb> args, ICommonSession admin, EntityUid carrier, string key, Func<string> act)
    {
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(key),
            Message = Loc.GetString(key + "-description", ("carrier", Name(carrier))),
            Category = VerbCategory.Admin,
            Icon = SkullIcon,
            Act = () => _popup.PopupCursor(act(), admin, PopupType.Medium),
            Impact = LogImpact.Medium,
        });
    }

    /// <summary>Whether a person can carry a mark of their own: a living body, not a ghost or a grid.</summary>
    public bool CanCarry(EntityUid uid)
    {
        return HasComp<MobStateComponent>(uid) && !HasComp<GhostComponent>(uid) && !HasComp<MapGridComponent>(uid);
    }

    /// <summary>Marks a ship or person for staff, an ordinary mark or a standing one, and tells them.</summary>
    public void StaffMark(EntityUid carrier, bool standing = false)
    {
        if (standing)
            MarkStanding(carrier);
        else
            Mark(carrier);

        TellAboard(carrier, standing ? "wg-zone-skull-staff-standing" : "wg-zone-skull-staff-marked");
    }

    /// <summary>
    /// Clears the mark on a ship or person for staff, and on whoever shares it: the ship's pilots, or the ship a pilot
    /// flies, which would pass it straight back otherwise. A partner's standing mark is kept, and named in
    /// <paramref name="kept"/>. Returns everyone cleared.
    /// </summary>
    public List<EntityUid> StaffClear(EntityUid carrier, out List<EntityUid> kept)
    {
        var cleared = new List<EntityUid> { carrier };
        kept = new List<EntityUid>();
        var pilots = EntityQueryEnumerator<PilotComponent>();
        while (pilots.MoveNext(out var pilot, out var comp))
        {
            if (comp.Console is not { } console || TerminatingOrDeleted(console) || Transform(console).GridUid is not { } ship)
                continue;

            EntityUid? partner = null;
            if (ship == carrier && pilot != carrier)
                partner = pilot;
            else if (pilot == carrier && ship != carrier && IsShip(ship))
                partner = ship;

            if (partner is not { } other)
                continue;

            if (CompOrNull<WGSkullComponent>(other) is { Staff: true })
                kept.Add(other);
            else
                cleared.Add(other);
        }

        foreach (var uid in cleared)
        {
            RemComp<WGWildernessDwellComponent>(uid);
            if (RemComp<WGSkullComponent>(uid))
                TellAboard(uid, "wg-zone-skull-staff-cleared");
        }

        return cleared;
    }

    /// <summary>Marks for staff and says so, for the admin's result line.</summary>
    public string StaffMarkText(EntityUid carrier, bool standing)
    {
        StaffMark(carrier, standing);
        return Loc.GetString(standing ? "cmd-wg_zone-standing" : "cmd-wg_zone-marked", ("carrier", ToPrettyString(carrier).ToString()));
    }

    /// <summary>Clears for staff and names everyone it came off, and any standing mark it left, for the admin's result line.</summary>
    public string StaffClearText(EntityUid carrier)
    {
        var cleared = StaffClear(carrier, out var kept);
        var text = Loc.GetString("cmd-wg_zone-cleared", ("carrier", Names(cleared)));
        if (kept.Count > 0)
            text += " " + Loc.GetString("cmd-wg_zone-kept", ("kept", Names(kept)), ("carrier", ToPrettyString(carrier).ToString()));

        return text;
    }

    private string Names(List<EntityUid> uids)
    {
        var names = new List<string>();
        foreach (var uid in uids)
            names.Add(ToPrettyString(uid).ToString());

        return string.Join(", ", names);
    }
}
