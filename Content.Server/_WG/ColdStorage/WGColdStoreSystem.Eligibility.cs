using System.Numerics;
using Content.Server._NF.StationEvents.Components;
using Content.Server._WG.Contracts;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.RadarMenu;
using Content.Server._WG.Remade.Corruption;
using Content.Server._WG.ShipAdmin;
using Content.Server._WG.ShipSquatters;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Worldgen.Components.Debris;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Audio.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Spawners;

namespace Content.Server._WG.ColdStorage;

/// <summary>Which grids may be stored, and who else holds a grid's uid.</summary>
public sealed partial class WGColdStoreSystem
{
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WGContractSystem _contracts = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private StationJobsSystem _stationJobs = default!;

    private const float MaxSpeed = 0.1f;
    private const float MaxSpin = 0.01f;

    private readonly List<EntityUid> _walk = new();
    private List<Entity<MapGridComponent>> _nearGrids = new();

    private EntityQuery<MetaDataComponent> _metaQuery;
    private EntityQuery<ActorComponent> _actorQuery;
    private EntityQuery<MindContainerComponent> _mindQuery;
    private EntityQuery<MobStateComponent> _mobQuery;

    private void InitializeEligibility()
    {
        _metaQuery = GetEntityQuery<MetaDataComponent>();
        _actorQuery = GetEntityQuery<ActorComponent>();
        _mindQuery = GetEntityQuery<MindContainerComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
    }

    /// <summary>
    /// The Fluent id of why a grid may not be stored now, or null if it may. Cheap tests first, the walk over its
    /// entities last. <paramref name="force"/> skips having been in stasis for the idle time; storing parks and pauses
    /// the grid anyway.
    /// </summary>
    public string? Ineligible(EntityUid grid, bool force)
    {
        if (_neverStore.TryGetValue(grid, out var never))
            return never;

        if (TerminatingOrDeleted(grid) || !TryComp<MapGridComponent>(grid, out var gridComp)
            || !TryComp<TransformComponent>(grid, out var xform))
            return "wg-coldstore-why-not-grid";

        if (_job?.Grid == grid)
            return "wg-coldstore-why-busy";

        var policy = _stasis.PolicyOf(grid, xform);
        if (policy != WGStasisPolicy.Ownerless && !(policy == WGStasisPolicy.PlayerShip && _playerShips))
            return "wg-coldstore-why-policy";

        if (HasComp<WGStoredGridComponent>(grid))
            return "wg-coldstore-why-busy";

        // A new player may be given a job on the ship and would arrive on bare floor.
        if (policy == WGStasisPolicy.PlayerShip && HasOpenJobs(grid))
            return "wg-coldstore-why-jobs";

        if (!force)
        {
            if (!_stasis.Grids.TryGetValue(grid, out var rec) || rec.State != WGGridStasisState.Paused
                || rec.StasisSince is not { } since || (_timing.CurTime - since).TotalSeconds < EffectiveIdle)
                return "wg-coldstore-why-not-idle";
        }

        if (_stasis.IsPinned(grid) || HasComp<FTLComponent>(grid))
            return "wg-coldstore-why-pinned";

        if (xform.MapUid is not { } map || MetaData(map).EntityPaused)
            return "wg-coldstore-why-map";

        foreach (var dock in _docking.GetDocks(grid))
        {
            if (dock.Comp.DockedWith != null)
                return "wg-coldstore-why-docked";
        }

        if (TryComp<PhysicsComponent>(grid, out var body)
            && (body.LinearVelocity.Length() >= MaxSpeed || MathF.Abs(body.AngularVelocity) >= MaxSpin))
            return "wg-coldstore-why-moving";

        var bounds = _transform.GetWorldMatrix(xform).TransformBox(gridComp.LocalAABB);
        if (NearestViewer(xform.MapID, bounds) <= _restoreRange)
            return "wg-coldstore-why-watched";

        if (GridNear(grid, xform.MapID, bounds))
            return "wg-coldstore-why-near-grid";

        if (ReferenceReason(grid) is { } referenced)
            return referenced;

        return WalkReason(grid);
    }

    /// <summary>Whether the grid's station has a job slot a joining player could take.</summary>
    private bool HasOpenJobs(EntityUid grid)
    {
        if (_station.GetOwningStation(grid) is not { } station || !HasComp<StationJobsComponent>(station))
            return false;

        foreach (var (_, slots) in _stationJobs.GetJobs(station))
        {
            if (slots is null or > 0)
                return true;
        }

        return false;
    }

    /// <summary>Metres from the bounds to the nearest viewer or announced arrival on the map.</summary>
    private float NearestViewer(MapId map, Box2 bounds)
    {
        var nearest = float.MaxValue;
        foreach (var viewer in _stasis.Viewers)
        {
            if (viewer.MapId != map)
                continue;

            nearest = MathF.Min(nearest, Distance(bounds, viewer.Position));
        }

        return nearest;
    }

    private static float Distance(Box2 bounds, Vector2 at)
    {
        var dx = MathF.Max(0f, MathF.Max(bounds.Left - at.X, at.X - bounds.Right));
        var dy = MathF.Max(0f, MathF.Max(bounds.Bottom - at.Y, at.Y - bounds.Top));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Whether another grid's bounds come within half the restore range of these.</summary>
    private bool GridNear(EntityUid? self, MapId map, Box2 bounds)
    {
        _nearGrids.Clear();
        _map.FindGridsIntersecting(map, bounds.Enlarged(_restoreRange / 2f), ref _nearGrids, approx: true, includeMap: false);
        foreach (var other in _nearGrids)
        {
            if (other.Owner != self)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether anything outside the grid holds its uid and would be left pointing at nothing: worldgen debris, contract
    /// wrecks, claims and deeds, game rules, admin event targets, corruption and NPC pilots. Systems that pin a grid
    /// while they work on it (magnets, drydocks, ropes, beams, hacks, autodock, hull retirement) are excluded by the pin.
    /// Paused holders count too, such as a claim in a locker on a grid in stasis.
    /// </summary>
    private string? ReferenceReason(EntityUid grid)
    {
        // The worldgen deletes and respawns its own debris with its chunk.
        if (HasComp<OwnedDebrisComponent>(grid))
            return "wg-coldstore-why-worldgen";

        foreach (var contract in _contracts.All)
        {
            if (contract.WreckGrid == grid || contract.ExtraWrecks.Contains(grid)
                || OnGrid(contract.Items, grid) || OnGrid(contract.Targets, grid) || OnGrid(contract.Anomalies, grid))
                return "wg-coldstore-why-contract";
        }

        var claims = AllEntityQuery<WGSalvageClaimComponent>();
        while (claims.MoveNext(out var claim))
        {
            if (claim.Ship == grid)
                return "wg-coldstore-why-claim";
        }

        var claimDrops = AllEntityQuery<WGSalvageClaimDropComponent>();
        while (claimDrops.MoveNext(out var drop))
        {
            if (drop.Ship == grid)
                return "wg-coldstore-why-claim";
        }

        var deeds = AllEntityQuery<WGEncounterDeedComponent>();
        while (deeds.MoveNext(out var deed))
        {
            if (deed.Ship == grid)
                return "wg-coldstore-why-claim";
        }

        var deedDrops = AllEntityQuery<WGEncounterDeedDropComponent>();
        while (deedDrops.MoveNext(out var deedDrop))
        {
            if (deedDrop.Ship == grid)
                return "wg-coldstore-why-claim";
        }

        var squatters = AllEntityQuery<WGShipSquatterComponent>();
        while (squatters.MoveNext(out var squatter))
        {
            if (squatter.Ship == grid)
                return "wg-coldstore-why-claim";
        }

        var rules = AllEntityQuery<RuleGridsComponent>();
        while (rules.MoveNext(out var rule))
        {
            var loaded = rule.MapGrids;
            if (loaded.Contains(grid))
                return "wg-coldstore-why-rule";
        }

        var errors = AllEntityQuery<BluespaceErrorRuleComponent>();
        while (errors.MoveNext(out var error))
        {
            var spawned = error.GridsUid;
            if (spawned.Contains(grid))
                return "wg-coldstore-why-rule";
        }

        var targets = AllEntityQuery<WGEventTargetComponent>();
        while (targets.MoveNext(out var target))
        {
            if (target.Grid == grid)
                return "wg-coldstore-why-rule";
        }

        var corruption = AllEntityQuery<WGCorruptionComponent>();
        while (corruption.MoveNext(out var tile))
        {
            if (tile.Grid == grid)
                return "wg-coldstore-why-corruption";
        }

        var pilots = AllEntityQuery<WGPilotDutyComponent>();
        while (pilots.MoveNext(out var pilot))
        {
            if (pilot.DockTarget == grid || pilot.FollowTarget == grid)
                return "wg-coldstore-why-npc";
        }

        var autodocks = AllEntityQuery<WGAutoDockComponent>();
        while (autodocks.MoveNext(out var autodock))
        {
            if (autodock.TargetGrid == grid)
                return "wg-coldstore-why-npc";
        }

        var ev = new WGColdStoreCheckEvent(grid);
        RaiseLocalEvent(ref ev);
        return ev.Reason;
    }

    private bool OnGrid(List<EntityUid> entities, EntityUid grid)
    {
        foreach (var uid in entities)
        {
            if (TryComp<TransformComponent>(uid, out var xform) && xform.GridUid == grid)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Walks everything aboard: nothing with a mind or a player, nothing that won't save but sounds and effects that
    /// are going anyway, and enough entities to be worth a stand-in.
    /// </summary>
    private string? WalkReason(EntityUid grid)
    {
        _walk.Clear();
        _walk.Add(grid);
        string? reason = null;
        for (var i = 0; i < _walk.Count; i++)
        {
            var uid = _walk[i];
            if (_actorQuery.HasComp(uid) || _mindQuery.TryComp(uid, out var mind) && mind.HasMind)
            {
                reason = "wg-coldstore-why-minded";
                break;
            }

            if (_metaQuery.GetComponent(uid).EntityPrototype is { MapSavable: false } && !Harmless(uid))
            {
                reason = "wg-coldstore-why-unsaved";
                break;
            }

            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _walk.Add(child);
            }
        }

        var count = _walk.Count;
        _walk.Clear();
        if (reason != null)
            return reason;

        return count < _minEntities ? "wg-coldstore-why-small" : null;
    }

    /// <summary>An unsaved entity a grid may lose: a sound, or an effect about to despawn that isn't a mob.</summary>
    private bool Harmless(EntityUid uid)
    {
        return HasComp<AudioComponent>(uid) || HasComp<TimedDespawnComponent>(uid) && !_mobQuery.HasComp(uid);
    }
}
