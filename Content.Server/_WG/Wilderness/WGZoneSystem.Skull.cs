using Content.Server._Mono.GridClaimer;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Wilderness;
using Content.Shared.Ghost;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.Wilderness;

/// <summary>
/// The Wilderness mark: a ship, or a person not aboard one, that stays in a Wilderness long enough is marked, held at
/// full while there and running down once out.
/// </summary>
public sealed partial class WGZoneSystem
{
    private static readonly Color SkullColor = Color.FromHex("#ff3b30");

    private readonly HashSet<EntityUid> _inWild = new();
    private readonly List<EntityUid> _drop = new();

    private TimeSpan _dwell;
    private TimeSpan _skullLength;
    private bool _onFoot;

    private void InitializeSkull()
    {
        Subs.CVar(_cfg, WildernessCVars.DwellSeconds, value => _dwell = TimeSpan.FromSeconds(value), true);
        Subs.CVar(_cfg, WildernessCVars.SkullMinutes, value => _skullLength = TimeSpan.FromMinutes(value), true);
        Subs.CVar(_cfg, WildernessCVars.SkullOnFoot, value => _onFoot = value, true);
    }

    private void UpdateSkulls(TimeSpan now)
    {
        _inWild.Clear();

        // Bought ships, crewed or not, then claimed sites.
        var ships = EntityQueryEnumerator<ShuttleDeedComponent, MapGridComponent, TransformComponent>();
        while (ships.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (IsShip(uid) && InWilderness(uid, xform))
                _inWild.Add(uid);
        }

        var claimed = EntityQueryEnumerator<ClaimableGridComponent, MapGridComponent, TransformComponent>();
        while (claimed.MoveNext(out var uid, out var claim, out _, out var xform))
        {
            if (claim.Claimed && IsShip(uid) && InWilderness(uid, xform))
                _inWild.Add(uid);
        }

        if (_onFoot)
        {
            foreach (var (subject, _) in _aboard)
            {
                if (HasComp<MapGridComponent>(subject) || HasComp<GhostComponent>(subject))
                    continue;

                if (InWilderness(subject, Transform(subject)))
                    _inWild.Add(subject);
            }

            // People already dwelling or marked whose player is away: a disconnect neither clears nor pauses the mark.
            var marked = EntityQueryEnumerator<WGSkullComponent>();
            while (marked.MoveNext(out var uid, out _))
                AddOnFoot(uid);

            var waiting = EntityQueryEnumerator<WGWildernessDwellComponent>();
            while (waiting.MoveNext(out var uid, out _))
                AddOnFoot(uid);
        }

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
            if (!_inWild.Contains(uid))
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
                TellAboard(uid, "wg-zone-skull-leaving");
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
    }

    /// <summary>Counts a person, not aboard a ship, as in the Wilderness if they are.</summary>
    private void AddOnFoot(EntityUid uid)
    {
        if (_inWild.Contains(uid)
            || HasComp<MapGridComponent>(uid)
            || HasComp<GhostComponent>(uid)
            || TerminatingOrDeleted(uid)
            || Subject(uid) != uid)
            return;

        if (InWilderness(uid, Transform(uid)))
            _inWild.Add(uid);
    }

    /// <summary>Whether a ship or person is in a Wilderness, and not in something that outranks it there.</summary>
    private bool InWilderness(EntityUid uid, TransformComponent xform)
    {
        var (centre, size) = Bounds(uid, xform);
        return FindZone(xform.MapID, centre, size, xform.GridUid) is { Zone.Skulls: true };
    }

    /// <summary>Tells everyone a mark is carried by: its ship's crew, or the person.</summary>
    private void TellAboard(EntityUid carrier, string key)
    {
        if (!_aboard.TryGetValue(carrier, out var sessions))
            return;

        var minutes = _skullLength.TotalMinutes;
        var message = Loc.GetString(key,
            ("ship", HasComp<MapGridComponent>(carrier) ? "yes" : "no"),
            ("minutes", minutes),
            ("open", _cfg.GetCVar(WildernessCVars.OpenToAll) ? "yes" : "no"));

        foreach (var session in sessions)
            Tell(session, message, SkullColor);
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
