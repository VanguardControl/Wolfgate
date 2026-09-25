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
using Content.Shared._WF.Planets;
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

/// <summary>The cut disc: extracting and dropping a chunk, and the watchdog that drops an orphan.</summary>
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

    /// <summary>Drops the chunk along with its falling hull.</summary>
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
            // The orphan wording carries no countdown.
            StartEvacuation(chunk, "wf-chunk-evac-orphan", 0);
            DropChunk(chunk);
        }

        // Drained after the enumeration because the cleanup deletes its grid synchronously.
        foreach (var chunk in _cleanupBuffer)
        {
            Cleanup(chunk);
        }
    }

    /// <summary>Whether the chunk's cracker is gone or not on the same orbit layer (by identity, not kind).</summary>
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

    /// <summary>Pushes the chunk down the z-stack, starting below the hull: equal progress breaks transit.</summary>
    public bool DropChunk(Entity<WFPlanetChunkComponent> ent, float startProgress = 0.98f)
    {
        if (ent.Comp.Dropped)
            return false;

        // PreventGridAnchorChanges stays so TryEnterTransit's Enable skips the chunk and can't unfix its rotation.
        RemComp<ForceAnchorComponent>(ent.Owner);
        EnsureComp<PreventGridAnchorChangesComponent>(ent.Owner);
        EnsureComp<ShuttleComponent>(ent.Owner);
        // ShuttleSystem.Enable minus SetFixedRotation(false): unfixing gives a negative inertia off-origin and asserts.
        if (TryComp<PhysicsComponent>(ent.Owner, out var dropBody))
        {
            _physics.SetBodyType(ent.Owner, BodyType.Dynamic, body: dropBody);
            _physics.SetBodyStatus(ent.Owner, dropBody, BodyStatus.InAir);
        }

        if (TryComp<PhysicsComponent>(ent.Owner, out var body) && body.BodyType == BodyType.Static)
            Log.Error($"{ToPrettyString(ent.Owner)} is still a static body after its chunk lock was released; it will not fall.");

        // A stale pooled-lift cache would hover the chunk straight back out of transit.
        _zLevels.WfInvalidateGravgenCapacity();

        var faller = EnsureComp<CEZGridFallerComponent>(ent.Owner);
        faller.Velocity = SharedWFCrackerSystem.FallSeedVelocity;
        faller.GravityTime = _timing.CurTime;

        // The central blast would land at the grid origin, far from the crater; the tile blasts do the work.
        faller.CrashIntensityPerTile = 0f;
        faller.CrashTileIntensity = ent.Comp.CrashTileIntensity;
        faller.CrashTileMaxIntensity = ent.Comp.CrashTileMaxIntensity;

        // The chunk lands where it hangs; the pose is snapshotted now and re-asserted at landing.
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

        // Only after admission succeeds, so a failure leaves the berth and its retry path intact.
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
