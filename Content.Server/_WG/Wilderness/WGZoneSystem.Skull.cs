using Content.Server._Mono.GridClaimer;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Wilderness;
using Content.Shared.Ghost;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.Wilderness;

/// <summary>
/// The Wilderness mark. Ships and people carry their own: a ship, or a person not aboard one, that stays in a Wilderness
/// long enough is marked; a mark holds at full while its carrier is in a Wilderness and runs down once out. A pilot and
/// the ship they fly share a mark: whichever is marked passes it, with its time left, to the other.
/// </summary>
public sealed partial class WGZoneSystem
{
    private static readonly Color SkullColor = Color.FromHex("#ff3b30");

    /// <summary>Ships and people in a Wilderness now, marked or not.</summary>
    private readonly HashSet<EntityUid> _inWild = new();

    /// <summary>Of those, the ones whose staying there marks them: ships, and people on foot.</summary>
    private readonly HashSet<EntityUid> _staying = new();

    private readonly List<EntityUid> _drop = new();
    private readonly List<(EntityUid Pilot, EntityUid Ship)> _pilots = new();

    private TimeSpan _dwell;
    private TimeSpan _skullLength;
    private bool _onFoot;

    private void InitializeSkull()
    {
        Subs.CVar(_cfg, WildernessCVars.DwellSeconds, value => _dwell = TimeSpan.FromSeconds(value), true);
        Subs.CVar(_cfg, WildernessCVars.SkullMinutes, value => _skullLength = TimeSpan.FromMinutes(value), true);
        Subs.CVar(_cfg, WildernessCVars.SkullOnFoot, value => _onFoot = value, true);
        InitializeAttacks();
    }

    private void UpdateSkulls(TimeSpan now)
    {
        _inWild.Clear();
        _staying.Clear();

        // Bought ships, crewed or not, then claimed sites.
        var ships = EntityQueryEnumerator<ShuttleDeedComponent, MapGridComponent, TransformComponent>();
        while (ships.MoveNext(out var uid, out _, out _, out var xform))
            AddShip(uid, xform);

        var claimed = EntityQueryEnumerator<ClaimableGridComponent, MapGridComponent, TransformComponent>();
        while (claimed.MoveNext(out var uid, out var claim, out _, out var xform))
        {
            if (claim.Claimed)
                AddShip(uid, xform);
        }

        // Every player, and anyone already dwelling or marked whose player is away: a disconnect neither clears nor
        // pauses the mark.
        foreach (var (mob, subject, _) in _alertsDue)
        {
            AddPerson(mob);
            if (subject != mob && !HasComp<MapGridComponent>(subject))
                AddPerson(subject); // A vehicle, which carries its crew as a ship does.
        }

        var marked = EntityQueryEnumerator<WGSkullComponent>();
        while (marked.MoveNext(out var uid, out _))
            AddPerson(uid);

        var waiting = EntityQueryEnumerator<WGWildernessDwellComponent>();
        while (waiting.MoveNext(out var uid, out _))
            AddPerson(uid);

        foreach (var carrier in _inWild)
        {
            if (TryComp<WGSkullComponent>(carrier, out var skull))
            {
                // Back in: held at full again.
                if (skull.ExpiresAt != null)
                {
                    skull.ExpiresAt = null;
                    Dirty(carrier, skull);
                }

                continue;
            }

            if (!_staying.Contains(carrier))
                continue;

            if (!TryComp<WGWildernessDwellComponent>(carrier, out var dwell))
            {
                dwell = AddComp<WGWildernessDwellComponent>(carrier);
                dwell.Since = now;
                Dirty(carrier, dwell);
                continue;
            }

            if (now - dwell.Since < _dwell)
                continue;

            RemComp<WGWildernessDwellComponent>(carrier);
            AddComp<WGSkullComponent>(carrier);
            TellAboard(carrier, "wg-zone-skull-marked");
        }

        _drop.Clear();
        var dwelling = EntityQueryEnumerator<WGWildernessDwellComponent>();
        while (dwelling.MoveNext(out var uid, out _))
        {
            if (!_staying.Contains(uid))
                _drop.Add(uid);
        }

        foreach (var uid in _drop)
            RemComp<WGWildernessDwellComponent>(uid);

        _drop.Clear();
        var skulls = EntityQueryEnumerator<WGSkullComponent>();
        while (skulls.MoveNext(out var uid, out var skull))
        {
            if (_inWild.Contains(uid))
                continue;

            if (skull.ExpiresAt is not { } expires)
            {
                skull.ExpiresAt = now + _skullLength;
                Dirty(uid, skull);
                TellAboard(uid, "wg-zone-skull-leaving", repeatable: true);
            }
            else if (now >= expires)
            {
                _drop.Add(uid);
            }
        }

        foreach (var uid in _drop)
        {
            RemComp<WGSkullComponent>(uid);
            TellAboard(uid, "wg-zone-skull-expired");
        }

        UpdatePilots(now);
    }

    /// <summary>Counts a ship as in the Wilderness if it is; staying there marks it.</summary>
    private void AddShip(EntityUid uid, TransformComponent xform)
    {
        if (!IsShip(uid) || !InWilderness(uid, xform))
            return;

        _inWild.Add(uid);
        _staying.Add(uid);
    }

    /// <summary>
    /// Counts a person, or a vehicle, as in the Wilderness if they are, aboard a ship or not. Staying there marks only
    /// those not aboard a ship, and only while <see cref="WildernessCVars.SkullOnFoot"/> is set.
    /// </summary>
    private void AddPerson(EntityUid uid)
    {
        if (_inWild.Contains(uid)
            || HasComp<MapGridComponent>(uid)
            || HasComp<GhostComponent>(uid)
            || TerminatingOrDeleted(uid))
            return;

        var subject = Subject(uid);
        if (!InWilderness(subject, Transform(subject)))
            return;

        _inWild.Add(uid);
        if (_onFoot && subject == uid)
            _staying.Add(uid);
    }

    /// <summary>A pilot and the ship they fly share a mark: the unmarked one takes the other's, with its time left.</summary>
    private void UpdatePilots(TimeSpan now)
    {
        _pilots.Clear();
        var query = EntityQueryEnumerator<PilotComponent>();
        while (query.MoveNext(out var uid, out var pilot))
        {
            if (pilot.Console is not { } console
                || TerminatingOrDeleted(console)
                || HasComp<GhostComponent>(uid)
                || Transform(console).GridUid is not { } ship
                || !IsShip(ship))
                continue;

            _pilots.Add((uid, ship));
        }

        foreach (var (pilot, ship) in _pilots)
        {
            var pilotSkull = CompOrNull<WGSkullComponent>(pilot);
            var shipSkull = CompOrNull<WGSkullComponent>(ship);
            if (pilotSkull != null && shipSkull == null)
                Inherit(ship, pilotSkull.ExpiresAt, now);
            else if (shipSkull != null && pilotSkull == null)
                Inherit(pilot, shipSkull.ExpiresAt, now);
        }
    }

    /// <summary>Gives a ship or person a mark passed on by its pilot or ship.</summary>
    private void Inherit(EntityUid carrier, TimeSpan? expires, TimeSpan now)
    {
        // A mark held in the Wilderness starts running at once for one that isn't there.
        if (expires == null && !InWildernessNow(carrier))
            expires = now + _skullLength;

        RemComp<WGWildernessDwellComponent>(carrier);
        var skull = EnsureComp<WGSkullComponent>(carrier);
        skull.ExpiresAt = expires;
        Dirty(carrier, skull);
        TellAboard(carrier, "wg-zone-skull-inherited");
    }

    /// <summary>Whether a ship or person is in a Wilderness now: a person by the ship they're aboard.</summary>
    private bool InWildernessNow(EntityUid uid)
    {
        var subject = Subject(uid);
        return InWilderness(subject, Transform(subject));
    }

    /// <summary>Whether a ship or person is in a Wilderness, and not in something that outranks it there.</summary>
    private bool InWilderness(EntityUid uid, TransformComponent xform)
    {
        var (centre, size) = Bounds(uid, xform);
        return FindZone(xform.MapID, centre, size, xform.GridUid, HasComp<MapGridComponent>(uid) ? uid : null) is { Zone.Skulls: true };
    }

    /// <summary>
    /// Tells whoever a mark is carried by: a person, or everyone aboard a ship or vehicle. A repeatable message, one
    /// that edge-hopping sends again and again, waits out its cooldown for each player.
    /// </summary>
    private void TellAboard(EntityUid carrier, string key, bool repeatable = false)
    {
        var minutes = _skullLength.TotalMinutes;
        var message = Loc.GetString(key,
            ("ship", HasComp<MapGridComponent>(carrier) ? "yes" : "no"),
            ("minutes", minutes),
            ("open", _cfg.GetCVar(WildernessCVars.OpenToAll) ? "yes" : "no"));

        if (TryComp<ActorComponent>(carrier, out var actor))
        {
            if (!repeatable || Due(actor.PlayerSession, key))
                Tell(actor.PlayerSession, message, SkullColor);
            return;
        }

        if (!_aboard.TryGetValue(carrier, out var sessions))
            return;

        foreach (var session in sessions)
        {
            if (!repeatable || Due(session, key))
                Tell(session, message, SkullColor);
        }
    }

    /// <summary>Marks a ship or person now, as if they had stayed in a Wilderness. For admins and tests.</summary>
    public void Mark(EntityUid carrier)
    {
        RemComp<WGWildernessDwellComponent>(carrier);
        var skull = EnsureComp<WGSkullComponent>(carrier);
        skull.ExpiresAt = _timing.CurTime + _skullLength;
        Dirty(carrier, skull);
    }
}
