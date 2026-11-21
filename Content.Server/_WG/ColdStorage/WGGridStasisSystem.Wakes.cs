using System.Numerics;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.Planets.Hop;
using Content.Server._WG.ShipShields;
using Content.Server.Shuttles.Events;
using Content.Shared._WG.Explosion;
using Robust.Server.Physics;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WG.ColdStorage;

/// <summary>Wakes a grid in stasis for what happens to it that attendance can't see: a split, a player taking a body, an attack.</summary>
public sealed partial class WGGridStasisSystem
{
    /// <summary>How long a grid that was shot, rammed or blown up stays awake.</summary>
    private static readonly TimeSpan AttackHold = TimeSpan.FromSeconds(120);

    /// <summary>How long a jump's exit counts as attended, which outlasts the longest jump.</summary>
    private static readonly TimeSpan ArrivalNotice = TimeSpan.FromSeconds(90);

    private void InitializeWakes()
    {
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<ExplosionShockwaveEvent>(OnShockwave);
        SubscribeLocalEvent<WGCrewHullHitEvent>(OnHullHit);
        SubscribeLocalEvent<WGShipShieldAttackedEvent>(OnShieldAttacked);
        SubscribeLocalEvent<WGShipCollisionEvent>(OnShipCollision);
        SubscribeLocalEvent<FTLStartedEvent>(OnFtlStarted);
        SubscribeLocalEvent<WGPlanetHopStartedEvent>(OnHopStarted);
    }

    /// <summary>A jump wakes the grids about its exit before the ship lands; the jumper itself is kept awake by its FTL component.</summary>
    private void OnFtlStarted(ref FTLStartedEvent args)
    {
        if (_enabled)
            NoteArrival(_transform.ToMapCoordinates(args.TargetCoordinates), ArrivalNotice);
    }

    private void OnHopStarted(ref WGPlanetHopStartedEvent args)
    {
        if (_enabled)
            NoteArrival(_transform.ToMapCoordinates(args.Target), ArrivalNotice);
    }

    /// <summary>The pieces of a split grid take the children it paused, which only its own record can wake.</summary>
    private void OnGridSplit(ref GridSplitEvent args)
    {
        if (_enabled && _grids.TryGetValue(args.Grid, out var rec) && rec.State != WGGridStasisState.Awake)
            Wake(args.Grid);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (_enabled && _xformQuery.TryComp(args.Entity, out var xform) && xform.GridUid is { } grid)
            WakeSleeping(grid, DefaultHold);
    }

    /// <summary>A blast wakes the grids near its epicentre; a silent one raises no shockwave and is not covered.</summary>
    private void OnShockwave(ref ExplosionShockwaveEvent args)
    {
        if (!_enabled || args.Epicenter.MapId == MapId.Nullspace)
            return;

        var radius = args.Iterations + 2f;
        _found.Clear();
        _map.FindGridsIntersecting(args.Epicenter.MapId, Box2.CenteredAround(args.Epicenter.Position, new Vector2(radius * 2f)),
            ref _found, approx: true, includeMap: false);
        foreach (var grid in _found)
        {
            WakeSleeping(grid.Owner, AttackHold);
        }

        _found.Clear();
    }

    private void OnHullHit(ref WGCrewHullHitEvent args)
    {
        WakeSleeping(args.Grid, AttackHold);
    }

    private void OnShieldAttacked(ref WGShipShieldAttackedEvent args)
    {
        WakeSleeping(args.Grid, AttackHold);
    }

    private void OnShipCollision(ref WGShipCollisionEvent args)
    {
        WakeSleeping(args.Grid, AttackHold);
        WakeSleeping(args.Other, AttackHold);
    }

    /// <summary>Wakes a grid only if it is in stasis, so an awake one gains no hold.</summary>
    private void WakeSleeping(EntityUid grid, TimeSpan hold)
    {
        if (IsInStasis(grid))
            Wake(grid, hold);
    }

    /// <summary>With stasis off, drops the pins and holds of deleted grids, which nothing else would.</summary>
    private void PruneIdle()
    {
        var now = _timing.CurTime;
        if (now < _nextRegistry)
            return;

        _nextRegistry = now + RegistryEvery;
        if (_pins.Count == 0 && _holds.Count == 0)
            return;

        _gone.Clear();
        foreach (var grid in _pins.Keys)
        {
            if (TerminatingOrDeleted(grid))
                _gone.Add(grid);
        }

        foreach (var grid in _holds.Keys)
        {
            if (TerminatingOrDeleted(grid))
                _gone.Add(grid);
        }

        foreach (var grid in _gone)
        {
            _pins.Remove(grid);
            _holds.Remove(grid);
        }

        _gone.Clear();
    }
}
