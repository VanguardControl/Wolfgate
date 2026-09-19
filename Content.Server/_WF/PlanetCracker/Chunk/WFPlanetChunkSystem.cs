using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.PlanetCracker.Anchors;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server.Decals;
using Content.Server.Gravity;
using Content.Server.Parallax;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Gravity;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.PlanetCracker.Chunk;

/// <summary>
/// The cut disc: the two crack hooks that create and drop one, the watchdog that drops an orphan and the drop itself.
/// Every subscription here is BROADCAST and by ref; the chunk component carries no directed subscription of its own,
/// which is what keeps the watchdog a sweep rather than a second owner of a (component, event) pair.
/// </summary>
public sealed partial class WFPlanetChunkSystem : EntitySystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private CEZGridConnectorSystem _connectors = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private GravitySystem _gravity = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private WFCrackerSystem _crackers = default!;
    [Dependency] private WFGravityAnchorSystem _anchors = default!;

    /// <summary>Next tick of the 1 Hz watchdog sweep.</summary>
    private TimeSpan _nextSweep;

    /// <summary>Chunks the sweep decided to drop, collected first so the drop may move grids mid-pass.</summary>
    private readonly List<Entity<WFPlanetChunkComponent>> _dropBuffer = new();

    /// <summary>Chunks whose transit admission failure has already been logged; cleared after a successful retry.</summary>
    private readonly HashSet<EntityUid> _transitFailureLogged = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // Crack events are broadcast by ref; the chunk's shutdown only clears failed-release bookkeeping.
        SubscribeLocalEvent<WFCrackCompletedEvent>(OnCrackCompleted);
        SubscribeLocalEvent<WFCrackerFallingEvent>(OnCrackerFalling);
        SubscribeLocalEvent<WFPlanetChunkComponent, ComponentShutdown>(OnChunkShutdown);

        InitializeDisconnect();
    }

    private void OnChunkShutdown(EntityUid uid, WFPlanetChunkComponent component, ComponentShutdown args)
    {
        _transitFailureLogged.Remove(uid);
    }

    /// <summary>The cut finished: everything that has to be true before a disc may be lifted, then the extraction.</summary>
    private void OnCrackCompleted(ref WFCrackCompletedEvent args)
    {
        if (!args.GroundMap.IsValid())
        {
            Log.Error($"{ToPrettyString(args.Cracker)} completed a crack with no ground map; no chunk was cut.");
            return;
        }

        if (!TryComp<WFPlanetCrackerComponent>(args.Cracker, out var cracker))
            return;

        var ent = new Entity<WFPlanetCrackerComponent>(args.Cracker, cracker);

        // A second raise for the same hull must not cut a second disc.
        if (TryGetChunk(ent, out _))
            return;

        if (_crackers.IsPlanetCracked(ent))
        {
            Log.Warning($"{ToPrettyString(args.Cracker)} completed a crack over an already cracked planet; no chunk was cut.");
            return;
        }

        if (!HasComp<MapGridComponent>(args.GroundMap) || !HasComp<BiomeComponent>(args.GroundMap))
        {
            Log.Error($"{ToPrettyString(args.GroundMap)} is not a biome-backed ground grid; {ToPrettyString(args.Cracker)} cut no chunk.");
            return;
        }

        if (!_crackers.TryGetBerthCentre(ent, out _))
        {
            Log.Error($"{ToPrettyString(args.Cracker)} has no resolvable chunk berth; no chunk was cut.");
            return;
        }

        TryExtract(ent, args.AnchorA, args.AnchorB, args.CentreXY, args.Radius, args.GroundMap, out _);
    }

    /// <summary>
    /// The hull is falling, so its chunk joins the same descent - at a DISTINCT progress. Two grids at identical
    /// progress fail the transit order-swap guard and are AABB-tested into an explosion every tick.
    /// </summary>
    private void OnCrackerFalling(ref WFCrackerFallingEvent args)
    {
        if (!TryComp<WFPlanetCrackerComponent>(args.Cracker, out var cracker))
            return;

        if (!TryGetChunk((args.Cracker, cracker), out var chunk))
            return;

        DropChunk(chunk);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + TimeSpan.FromSeconds(1);

        _dropBuffer.Clear();
        _cleanupBuffer.Clear();

        var query = EntityQueryEnumerator<WFPlanetChunkComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Dropped)
            {
                UpdateDropped((uid, comp));
                continue;
            }

            // Still in the berth: the chunk's own release beats, for the people standing on it.
            UpdateEvacuating((uid, comp));

            // ExtractedAt is paused with its map, so a paused berth cannot burn the whole grace at once.
            if (_timing.CurTime < comp.ExtractedAt + comp.WatchdogGrace)
                continue;

            var chunkMap = Transform(uid).MapUid;

            // A chunk already falling is on a transit map of its own and is nobody's orphan.
            if (chunkMap is { } transit && HasComp<CEZTransitMapComponent>(transit))
                continue;

            if (ShouldDrop((uid, comp), chunkMap))
                _dropBuffer.Add((uid, comp));
        }

        foreach (var chunk in _dropBuffer)
        {
            // Design D24, "with the evacuation alarm": the orphan wording, which carries no countdown.
            StartEvacuation(chunk, "wf-chunk-evac-orphan", 0);
            DropChunk(chunk);
        }

        // Drained after the enumeration because the cleanup deletes its grid synchronously.
        foreach (var chunk in _cleanupBuffer)
        {
            Cleanup(chunk);
        }
    }

    /// <summary>
    /// Design D24, read literally: the chunk drops when its cracker is gone or is no longer on the SAME orbit layer.
    /// The identity test is the point - every planet has an orbit layer and the orbit layer is an FTL destination, so a
    /// hull that jumps to another planet's orbit passes a kind test while abandoning its chunk.
    /// </summary>
    private bool ShouldDrop(Entity<WFPlanetChunkComponent> ent, EntityUid? chunkMap)
    {
        if (ent.Comp.Cracker is not { } netCracker || !TryGetEntity(netCracker, out var cracker))
            return true;

        if (TerminatingOrDeleted(cracker.Value) || EntityManager.IsQueuedForDeletion(cracker.Value))
            return true;

        var crackerMap = Transform(cracker.Value).MapUid;

        if (crackerMap != chunkMap)
            return true;

        // The secondary guard: the shared map must still actually be an orbit layer.
        if (crackerMap is not { } map || !HasComp<WFOrbitLayerComponent>(map))
            return true;

        // And it must be the berth's own recorded map, so a chunk moved off its layer by anything else is caught too.
        if (ent.Comp.OrbitMap is { } netOrbit && TryGetEntity(netOrbit, out var orbit) && orbit != chunkMap)
            return true;

        return false;
    }

    /// <summary>
    /// Pushes the chunk down the planet's z-stack: F4's hull fall order minus the gravity generator step, which a chunk
    /// does not have. The default start progress is deliberately below the hull's 1.0.
    /// </summary>
    public bool DropChunk(Entity<WFPlanetChunkComponent> ent, float startProgress = 0.98f)
    {
        if (ent.Comp.Dropped)
            return false;

        // The force-anchor goes, but PreventGridAnchorChanges STAYS: TryEnterTransit's own per-grid Enable is
        // un-forced and that component is what makes it skip the chunk. Let through, it would unfix the chunk's
        // rotation, and ResetMassData then asserts the server down on any site away from the planet origin (the
        // same negative inertia the extraction guards against). The body is made dynamic by hand just below instead.
        RemComp<ForceAnchorComponent>(ent.Owner);
        EnsureComp<PreventGridAnchorChangesComponent>(ent.Owner);
        EnsureComp<ShuttleComponent>(ent.Owner);
        // ShuttleSystem.Enable minus its SetFixedRotation(false): the chunk keeps fixed rotation for life (see the
        // extraction's note on ResetMassData), because unfixing it recomputes an inertia that goes negative on any
        // site away from the planet origin and asserts the server down. A falling disc has no use for spin anyway.
        if (TryComp<PhysicsComponent>(ent.Owner, out var dropBody))
        {
            _physics.SetBodyType(ent.Owner, BodyType.Dynamic, body: dropBody);
            _physics.SetBodyStatus(ent.Owner, dropBody, BodyStatus.InAir);
        }

        if (TryComp<PhysicsComponent>(ent.Owner, out var body) && body.BodyType == BodyType.Static)
            Log.Error($"{ToPrettyString(ent.Owner)} is still a static body after its chunk lock was released; it will not fall.");

        // The sweep rebuilds its pooled-lift cache only twice a second, and the hover branch exits transit at
        // progress >= 0.99 with |velocity| <= 0.1, so a stale cache would settle the chunk straight back up.
        _zLevels.WfInvalidateGravgenCapacity();

        var faller = EnsureComp<CEZGridFallerComponent>(ent.Owner);
        faller.Velocity = SharedWFCrackerSystem.FallSeedVelocity;
        faller.GravityTime = _timing.CurTime;

        // The engine's own central blast, suppressed. CEZLevelsSystem.Gravity.cs:426-430 queues it through the
        // EntityUid overload, which resolves the epicentre as the grid's OWN coordinates (ExplosionSystem.cs:295-320),
        // and TryExtract set this grid's origin to the GROUND grid's origin so the tile indices would match
        // (Extraction.cs:141-142) - so on a real biome planet it detonates hundreds of tiles from the crater. Zero
        // makes it a no-op through the totalIntensity <= 0 early return at ExplosionSystem.cs:374.
        // No replacement blast is queued at the crater either: QueueExplosion merges a same-prototype explosion within
        // MaxCombineDistance (1f for Default) of a still-queued one by ADDING TotalIntensity and discarding the
        // incoming slope and maxTileIntensity (ExplosionSystem.cs:386-400), and the per-tile crash blasts stay queued
        // for many seconds under the processing throttle (ExplosionSystem.Processing.cs:95-107), so a second blast at
        // the crater would simply be absorbed - arithmetically identical to raising CrashTileIntensity.
        faller.CrashIntensityPerTile = 0f;
        faller.CrashTileIntensity = ent.Comp.CrashTileIntensity;
        faller.CrashTileMaxIntensity = ent.Comp.CrashTileMaxIntensity;

        // The chunk falls from exactly where it hangs - clear of the hull, turned to its heading - and lands wherever
        // that is, hole or not: where the rig carried it is where it comes down (playtest decision). Snapshotted
        // before the grid moves; the chunk is dynamic for the whole fall, so the pose is re-asserted once at landing
        // rather than trusted to survive it.
        var (dropPos, dropRot) = _transform.GetWorldPositionRotation(ent.Owner);
        ent.Comp.DropWorldPos = dropPos;
        ent.Comp.DropWorldRot = dropRot;

        if (!TryComp<MapGridComponent>(ent.Owner, out var grid))
        {
            if (_transitFailureLogged.Add(ent.Owner))
                Log.Error($"{ToPrettyString(ent.Owner)} tried to drop but is not a grid.");

            RestoreParkedChunk(ent);
            return false;
        }

        if (!_zLevels.TryEnterTransit((ent.Owner, grid), startProgress))
        {
            // It returns false without logging when the map is not a z-map or is already a transit map.
            if (_transitFailureLogged.Add(ent.Owner))
                Log.Warning($"{ToPrettyString(ent.Owner)} could not be pushed into transit for its chunk drop; retrying.");

            RestoreParkedChunk(ent);
            return false;
        }

        _transitFailureLogged.Remove(ent.Owner);
        ent.Comp.EnteredTransit = true;

        // The gangway goes after transit admission succeeds: a failed admission must leave the berth and its retry
        // path intact.
        LiftGangway(ent);

        ent.Comp.Dropped = true;
        Dirty(ent);

        // The hull stops owning a chunk it no longer holds, so a later crack reads a clean back-link.
        if (ent.Comp.Cracker is { } netCracker
            && TryGetEntity(netCracker, out var cracker)
            && TryComp<WFPlanetCrackerComponent>(cracker, out var crackerComp)
            && crackerComp.Chunk == GetNetEntity(ent.Owner))
        {
            crackerComp.Chunk = null;
            Dirty(cracker.Value, crackerComp);
        }

        ent.Comp.DropStream = _audio.PlayPvs(ent.Comp.DropSound, ent.Owner, AudioParams.Default.WithLoop(true))?.Entity;

        if (TryComp<GravityComponent>(ent.Owner, out var gravity))
            _gravity.StartGridShake(ent.Owner, gravity);

        var ev = new WFChunkDroppedEvent(ent.Owner, ent.Comp.Cracker is { } net && TryGetEntity(net, out var owner)
            ? owner.Value
            : EntityUid.Invalid);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Restores a parked chunk after transit admission failed so the watchdog can retry it later.</summary>
    private void RestoreParkedChunk(Entity<WFPlanetChunkComponent> ent)
    {
        EnsureComp<ForceAnchorComponent>(ent.Owner);
        EnsureComp<PreventGridAnchorChangesComponent>(ent.Owner);
        _shuttle.Disable(ent.Owner, force: true);

        if (TryComp<PhysicsComponent>(ent.Owner, out var body))
        {
            _physics.SetBodyType(ent.Owner, BodyType.Static, body: body);
            _physics.SetBodyStatus(ent.Owner, body, BodyStatus.OnGround);
        }
    }

    /// <summary>The chunk hanging in this hull's berth, if it still has one.</summary>
    public bool TryGetChunk(Entity<WFPlanetCrackerComponent> cracker, out Entity<WFPlanetChunkComponent> chunk)
    {
        chunk = default;

        if (cracker.Comp.Chunk is not { } net || !TryGetEntity(net, out var uid))
            return false;

        if (!TryComp<WFPlanetChunkComponent>(uid, out var comp))
            return false;

        chunk = (uid.Value, comp);
        return true;
    }
}
