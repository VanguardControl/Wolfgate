using Content.Server._WG.NpcCrew;
using Content.Shared._WG.Wilderness;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.Wilderness;

/// <summary>
/// Attacking into a Wilderness: a ship hit inside one by a ship outside marks the attacking ship, and everyone who fired
/// its guns lately, as if they had stayed there.
/// </summary>
public sealed partial class WGZoneSystem
{
    /// <summary>How long a gunner stays answerable for the rounds their ship's guns have in flight.</summary>
    private static readonly TimeSpan GunnerMemory = TimeSpan.FromSeconds(60);

    /// <summary>A running mark is only topped up once it has run down this much, so a barrage doesn't resend it.</summary>
    private static readonly TimeSpan TopUp = TimeSpan.FromSeconds(1);

    /// <summary>Who last fired each ship's guns from a gunnery console, and when.</summary>
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, TimeSpan>> _gunners = new();

    private readonly List<EntityUid> _staleGunners = new();
    private readonly List<EntityUid> _oldGunners = new();

    private void InitializeAttacks()
    {
        SubscribeLocalEvent<WGCrewHullHitEvent>(OnHullHit);
    }

    /// <summary>Notes that a player fired a ship's guns from one of its gunnery consoles.</summary>
    public void NoteGunner(EntityUid ship, EntityUid gunner)
    {
        if (!HasComp<ActorComponent>(gunner))
            return;

        if (!_gunners.TryGetValue(ship, out var gunners))
            _gunners[ship] = gunners = new Dictionary<EntityUid, TimeSpan>();

        gunners[gunner] = _timing.CurTime;
    }

    private void OnHullHit(ref WGCrewHullHitEvent args)
    {
        var target = args.Grid;
        var attacker = args.AttackerGrid;
        if (target == attacker
            || !IsShip(target)
            || !HasComp<MapGridComponent>(attacker)
            || TerminatingOrDeleted(attacker)
            || !InWilderness(target, Transform(target))
            || InWilderness(attacker, Transform(attacker)))
            return;

        if (IsShip(attacker))
            MarkForAttack(attacker);

        if (!_gunners.TryGetValue(attacker, out var gunners))
            return;

        var since = _timing.CurTime - GunnerMemory;
        foreach (var (gunner, at) in gunners)
        {
            if (at >= since && !TerminatingOrDeleted(gunner))
                MarkForAttack(gunner);
        }
    }

    /// <summary>Marks a ship or person for attacking into a Wilderness, or tops up a mark already running down.</summary>
    private void MarkForAttack(EntityUid carrier)
    {
        var expires = _timing.CurTime + _skullLength;
        if (TryComp<WGSkullComponent>(carrier, out var skull))
        {
            // A held mark is already at full.
            if (skull.ExpiresAt is not { } current || current > expires - TopUp)
                return;

            skull.ExpiresAt = expires;
            Dirty(carrier, skull);
            return;
        }

        RemComp<WGWildernessDwellComponent>(carrier);
        skull = AddComp<WGSkullComponent>(carrier);
        skull.ExpiresAt = expires;
        Dirty(carrier, skull);
        TellAboard(carrier, "wg-zone-skull-attacked");
    }

    /// <summary>Forgets gunners whose rounds have long landed, and ships that are gone.</summary>
    private void UpdateGunners(TimeSpan now)
    {
        _staleGunners.Clear();
        var since = now - GunnerMemory;
        foreach (var (ship, gunners) in _gunners)
        {
            if (TerminatingOrDeleted(ship))
            {
                _staleGunners.Add(ship);
                continue;
            }

            _oldGunners.Clear();
            foreach (var (gunner, at) in gunners)
            {
                if (at < since)
                    _oldGunners.Add(gunner);
            }

            foreach (var gunner in _oldGunners)
                gunners.Remove(gunner);

            if (gunners.Count == 0)
                _staleGunners.Add(ship);
        }

        foreach (var ship in _staleGunners)
            _gunners.Remove(ship);
    }
}
