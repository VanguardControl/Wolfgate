using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._NF.Shuttles.Components;
using Content.Server._Mono.Cleanup;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Server.Atmos.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using PhysTransform = Robust.Shared.Physics.Transform;

namespace Content.Server._WF.PlanetCracker.Chunk;

/// <summary>
/// The extraction itself: read the disc, build the grid, move what is standing on it, empty and pin the hole, decal the
/// rim, hang the chunk in the berth and park it. The hole is Tile.Empty and nothing but the biome pin keeps it.
/// </summary>
public sealed partial class WFPlanetChunkSystem
{
    /// <summary>Tiles of open space kept between the hull's furthest edge and the disc's near edge.</summary>
    public const float BerthClearance = 2f;

    /// <summary>Rim decal on a stretch of ring that runs along an axis.</summary>
    private const string RimStraightDecal = "WFCrackRimStraight";

    /// <summary>Rim decal on a stretch of ring that turns a corner.</summary>
    private const string RimCurveDecal = "WFCrackRimCurve";

    /// <summary>Accumulator the biome reserve fills; cleared before every call because ReserveTiles does not clear it.</summary>
    private readonly List<(Vector2i Index, Tile Tile)> _tileBuffer = new();

    /// <summary>The whole Tile struct of every disc tile, copied verbatim so Variant and RotationMirroring survive.</summary>
    private readonly List<(Vector2i Index, Tile Tile)> _chunkTiles = new();

    /// <summary>The hole stamp: every disc index at Tile.Empty, written in one SetTiles.</summary>
    private readonly List<(Vector2i Index, Tile Tile)> _holeTiles = new();

    /// <summary>The ONE definition of "inside the circle"; the tile copy, the entity move and the pin all read it.</summary>
    private readonly HashSet<Vector2i> _holeIndices = new();

    /// <summary>The ring of ground just outside the circle, which keeps its biome tile and carries the decals.</summary>
    private readonly List<Vector2i> _rimIndices = new();

    /// <summary>Lookup result over the cut circle; a fixture-overlap prefilter, never the membership test.</summary>
    private readonly HashSet<EntityUid> _moveSet = new();

    /// <summary>What actually moved onto the chunk, with the tile index it vacated, for the biome bookkeeping.</summary>
    private readonly List<(EntityUid Uid, Vector2i Index)> _moved = new();

    /// <summary>Rim decal ids, buffered until the chunk component exists to hold them.</summary>
    private readonly List<uint> _rimDecals = new();

    /// <summary>The cracker plus everything docked to it; ignored by the clearance check and by Smimsh.</summary>
    private readonly HashSet<EntityUid> _ignored = new();

    /// <summary>Grids over the berth; not readonly, the grid query takes it by ref.</summary>
    private List<Entity<MapGridComponent>> _found = new();

    /// <summary>
    /// Cuts the disc free and hangs it in the hull's berth. Every step is ordered: the terrain is forced to exist
    /// before it is read, the entities move before the source is emptied, and the rim is decalled after the stamp.
    /// </summary>
    public bool TryExtract(
        Entity<WFPlanetCrackerComponent> cracker,
        EntityUid anchorA,
        EntityUid anchorB,
        Vector2 centre,
        float radius,
        EntityUid groundMap,
        out EntityUid chunk)
    {
        chunk = EntityUid.Invalid;

        if (!TryComp<MapGridComponent>(groundMap, out var groundGrid) || !TryComp<BiomeComponent>(groundMap, out var biomeComp))
        {
            Log.Error($"{ToPrettyString(groundMap)} is not a biome-backed ground grid; {ToPrettyString(cracker.Owner)} cut no chunk.");
            return false;
        }

        if (!_crackers.TryGetBerthCentre(cracker, out var berth))
        {
            Log.Error($"{ToPrettyString(cracker.Owner)} has no resolvable chunk berth; no chunk was cut.");
            return false;
        }

        if (Transform(cracker.Owner).MapUid is not { } orbitMap)
        {
            Log.Error($"{ToPrettyString(cracker.Owner)} is on no map; no chunk was cut.");
            return false;
        }

        var biome = new Entity<BiomeComponent>(groundMap, biomeComp);
        var groundXform = Transform(groundMap);
        var groundMapId = groundXform.MapID;
        var (groundWorldPos, groundWorldRot) = _transform.GetWorldPositionRotation(groundXform);

        // A ground layer is identity, so local and world agree and the disc AABB needs no transform.
        var aabb = Box2.CenteredAround(centre, new Vector2(radius * 2f, radius * 2f));
        var radiusSq = radius * radius;
        var half = groundGrid.TileSizeHalfVector;

        // STEP 1: force the terrain to exist. Enlarged by one so the rim ring and all four corner biome chunks are
        // generated and pinned in the same pass, and read in the same tick so nothing can unload in between.
        _tileBuffer.Clear();
        _biome.ReserveTiles(groundMap, aabb.Enlarged(1f), _tileBuffer, mapGrid: groundGrid);

        // STEP 2: read the disc by tile CENTRE and build the one membership set. ignoreEmpty:false is mandatory - an
        // empty tile inside the circle still has to be pinned, or the biome refills the hole.
        _chunkTiles.Clear();
        _holeIndices.Clear();

        var stillEmpty = 0;
        var tiles = _map.GetLocalTilesEnumerator(groundMap, groundGrid, aabb, ignoreEmpty: false);

        while (tiles.MoveNext(out var tileRef))
        {
            if (((Vector2)tileRef.GridIndices + half - centre).LengthSquared() > radiusSq)
                continue;

            // The whole struct: Tile.WithVariant would drop RotationMirroring and the biome's look with it.
            _chunkTiles.Add((tileRef.GridIndices, tileRef.Tile));
            _holeIndices.Add(tileRef.GridIndices);

            if (tileRef.Tile.IsEmpty)
                stillEmpty++;
        }

        if (stillEmpty > 0)
            Log.Warning($"{stillEmpty} of {_holeIndices.Count} tiles inside the cut circle on {ToPrettyString(groundMap)} were still empty after the biome reserve.");

        // STEP 3: the chunk is created on the GROUND map and re-parented later. A grid created straight onto the orbit
        // map gets neither CEZPhysics nor a faller: its GridAddEvent fires in nullspace and nothing re-sweeps it.
        var chunkEnt = _mapManager.CreateGridEntity(groundMapId);
        _transform.SetWorldPositionRotation(chunkEnt.Owner, groundWorldPos, groundWorldRot);

        // Before the first SetTiles: a disc containing a lake or a chasm ring is not simply connected and the fixture
        // system would tear it into several grids the instant it is filled.
        chunkEnt.Comp.CanSplit = false;

        // Also before the first SetTiles: the chunk is world-aligned, so its tiles sit at the ground's own indices,
        // hundreds of tiles from the grid origin on any site away from the planet centre. ResetMassData then does
        // inertia -= mass * |centre|^2 in floats, which cancels below zero and trips the engine's assert
        // (SharedPhysicsSystem.Components.cs ResetMassData) on the tile write. The chunk never rotates: parked it is
        // static, falling it goes straight down. A fixed-rotation body skips the inertia term altogether.
        _physics.SetFixedRotation(chunkEnt.Owner, true);

        // STEP 4: one SetTiles, then drop the atmosphere the mass change just earned it.
        _map.SetTiles(chunkEnt.Owner, chunkEnt.Comp, _chunkTiles);
        RemComp<GridAtmosphereComponent>(chunkEnt.Owner);

        // STEP 5: move the riders BEFORE the source is emptied - an emptied map chunk is deleted outright.
        MoveRiders(groundMap, groundGrid, chunkEnt, centre, radius);

        // STEP 6: the biome must forget the props that left, or the entries leak and their tiles regenerate.
        foreach (var (uid, index) in _moved)
        {
            _biome.WfForgetLoadedEntity(biome, uid, index);
        }

        // STEP 7: stamp the hole. Tile.Empty is the only value anything falls through, and there is no rim tile.
        _holeTiles.Clear();

        foreach (var index in _holeIndices)
        {
            _holeTiles.Add((index, Tile.Empty));
        }

        _map.SetTiles(groundMap, groundGrid, _holeTiles);

        // STEP 8: pin the hole and the rim. A fully emptied map chunk is removed and GetTileRef then fabricates
        // Tile.Empty again, so ModifiedTiles is the only thing standing between the hole and a regenerated surface.
        _biome.WfPinTiles(biome, _holeIndices);

        BuildRim(groundMap, groundGrid, centre, radius);
        _biome.WfPinTiles(biome, _rimIndices);

        // STEP 9: the decal ring, after the stamp, on pinned biome ground just outside the circle.
        StampRim(groundMap, centre, half);

        // Built before the berth pose, because the pose clears every hull in this set, step 11's Smimsh reads the same
        // set and anything missing from it is CrushGrid'd - starting with the hull that just cut the chunk.
        _ignored.Clear();
        _shuttle.GetAllDockedShuttles(cracker.Owner, _ignored);

        // STEP 10: hang it in the berth, turned about its own disc centre to the hull's heading so its tiles line up
        // with the deck and the gangway. The hole pose is not kept: the drop re-poses the chunk over the hole first.
        // The circle centre is world XY; the chunk's own frame is the ground's, so it is taken back to ground-local
        // before the hull's heading is put on it.
        var hang = GetBerthCentreClearOf(cracker, berth, radius, _ignored);
        var hullRot = _transform.GetWorldRotation(cracker.Owner);
        var localCentre = (-groundWorldRot).RotateVec(centre - groundWorldPos);
        var target = hang - hullRot.RotateVec(localCentre);

        WarnOnObstruction(cracker, chunkEnt, berth.MapId, target, _ignored);

        var groundDepth = TryComp<CEZMapComponent>(groundMap, out var groundZ) ? groundZ.Depth : 0;
        var orbitDepth = TryComp<CEZMapComponent>(orbitMap, out var orbitZ) ? orbitZ.Depth : 0;

        _zLevels.WfMoveGridToLayer(
            (chunkEnt.Owner, chunkEnt.Comp),
            orbitMap,
            target,
            hullRot,
            offset: orbitDepth - groundDepth,
            depth: orbitDepth);

        // STEP 11: clear the berth of loose props. The cracker and its docked set are ignored, or the 0.2-tile fixture
        // enlargement on an overlapping berth would crush the hull that cut the chunk.
        _shuttle.Smimsh(chunkEnt.Owner, explodeGrids: true, ignoredGrids: _ignored);

        // STEP 12: park and furnish, in this order.
        ParkChunk(chunkEnt);

        var comp = AddComp<WFPlanetChunkComponent>(chunkEnt.Owner);
        comp.Cracker = GetNetEntity(cracker.Owner);
        comp.GroundMap = GetNetEntity(groundMap);
        comp.OrbitMap = GetNetEntity(orbitMap);
        comp.HoleCentre = centre;
        comp.Radius = radius;
        comp.TileCount = _chunkTiles.Count;
        comp.ExtractedAt = _timing.CurTime;
        comp.RimDecals.AddRange(_rimDecals);
        Dirty(chunkEnt.Owner, comp);

        cracker.Comp.Chunk = GetNetEntity(chunkEnt.Owner);
        Dirty(cracker);

        LayGangway(cracker, (chunkEnt.Owner, comp), hang, radius, _ignored);

        // The connector resolves its links by world position and never subscribes MoveEvent, so a new grid on a layer
        // has to ask for the recalculation itself.
        _connectors.MarkDirty();

        // STEP 13: flag the planet. A site with no sector body is loud rather than silently re-crackable.
        FlagPlanet(cracker, chunkEnt.Owner, orbitMap, groundMap);

        // STEP 14: everything the crew sees and hears, then the hook.
        PlayExtractionEffects(cracker, chunkEnt.Owner, groundMap, orbitMap, centre, radius);

        var ev = new WFChunkExtractedEvent(chunkEnt.Owner, cracker.Owner, groundMap, centre, radius);
        RaiseLocalEvent(ref ev);

        chunk = chunkEnt.Owner;
        return true;
    }

    /// <summary>
    /// Moves everything standing inside the circle onto the chunk grid.
    /// The lookup is a fixture-overlap query and is only a prefilter: membership is decided by the entity's own tile
    /// index against the disc set, because a rim-straddling mob would otherwise ride up onto a tile that was never
    /// copied and fall straight through the chunk's floor.
    /// </summary>
    private void MoveRiders(
        EntityUid groundMap,
        MapGridComponent groundGrid,
        Entity<MapGridComponent> chunk,
        Vector2 centre,
        float radius)
    {
        _moveSet.Clear();
        _moved.Clear();

        // Uncontained is Dynamic | Static | Sundries | Sensors, so a crate's contents ride with the crate.
        _lookup.GetLocalEntitiesIntersecting(
            groundMap,
            new PhysShapeCircle(radius, centre),
            PhysTransform.Empty,
            _moveSet,
            LookupFlags.Uncontained);

        foreach (var uid in _moveSet)
        {
            if (uid == groundMap || uid == chunk.Owner)
                continue;

            var xform = Transform(uid);

            if (xform.ParentUid != groundMap)
                continue;

            // A landed transport is deliberately left behind: dragging a grid would drag its docks and its z-network.
            if (HasComp<MapGridComponent>(uid))
                continue;

            var index = _map.TileIndicesFor(groundMap, groundGrid, xform.Coordinates);

            if (!_holeIndices.Contains(index))
                continue;

            // An in-circle tile the biome never generated is copied as Tile.Empty, and AddToSnapGridCell refuses an
            // empty destination. Checked BEFORE the unanchor, because the unanchor cannot be taken back: an anchored
            // rider would otherwise end up detached but still Static, with a gravity anchor's state change swallowed
            // by the ride set and its pair left claimed.
            if (xform.Anchored && _map.GetTileRef(chunk.Owner, chunk.Comp, index).Tile.IsEmpty)
            {
                Log.Error($"Chunk tile {index} is empty, so anchored {ToPrettyString(uid)} could not ride up; it was left anchored on the ground map.");
                continue;
            }

            // An anchor's own unanchor/re-anchor would dissolve its pair and abort the cut; suppress its handler for
            // both halves of the move.
            var isAnchor = HasComp<WFGravityAnchorComponent>(uid);

            if (isAnchor)
                _anchors.BeginChunkRide(uid);

            if (xform.Anchored)
            {
                _transform.Unanchor(uid, xform, setPhysics: false);

                // The cross-grid overload: the convenience one demands a matching GridUid. A false return means the
                // destination tile is EMPTY, which the disc filter makes unreachable, so it is an error and a skip -
                // never a re-parent over vacuum.
                if (!_transform.AnchorEntity((uid, xform), (chunk.Owner, chunk.Comp), index))
                {
                    Log.Error($"Could not anchor {ToPrettyString(uid)} onto chunk tile {index}; it was left on the ground map.");

                    // Back where it came from, still inside the ride suppression: the unanchor above already happened
                    // and leaving the rider detached but Static is worse than an unmoved anchor.
                    _transform.AnchorEntity((uid, xform), (groundMap, groundGrid), index);

                    if (isAnchor)
                        _anchors.EndChunkRide(uid);

                    continue;
                }
            }
            else
            {
                _transform.SetParent(uid, xform, chunk.Owner);
            }

            if (isAnchor)
                _anchors.EndChunkRide(uid);

            _moved.Add((uid, index));
        }
    }

    /// <summary>The ring of indices whose tile centre falls in (radius, radius + 1]; the decals' ground.</summary>
    private void BuildRim(EntityUid groundMap, MapGridComponent groundGrid, Vector2 centre, float radius)
    {
        _rimIndices.Clear();

        var half = groundGrid.TileSizeHalfVector;
        var innerSq = radius * radius;
        var outerSq = (radius + 1f) * (radius + 1f);
        var bounds = Box2.CenteredAround(centre, new Vector2(radius * 2f, radius * 2f)).Enlarged(1f);

        var tiles = _map.GetLocalTilesEnumerator(groundMap, groundGrid, bounds, ignoreEmpty: false);

        while (tiles.MoveNext(out var tileRef))
        {
            var distanceSq = ((Vector2)tileRef.GridIndices + half - centre).LengthSquared();

            if (distanceSq <= innerSq || distanceSq > outerSq)
                continue;

            _rimIndices.Add(tileRef.GridIndices);
        }
    }

    /// <summary>
    /// Stamps the rim ring. A decal's coordinates are its texture's bottom-left corner in the grid's own frame, so the
    /// tile index itself is the right anchor for a tile-sized decal, and the rotation comes from the Angle argument
    /// because the overlay only ever draws frame zero.
    /// </summary>
    private void StampRim(EntityUid groundMap, Vector2 centre, Vector2 half)
    {
        _rimDecals.Clear();

        var refused = 0;

        foreach (var index in _rimIndices)
        {
            var normal = (Vector2)index + half - centre;

            if (normal.LengthSquared() <= 0f)
                continue;

            var tangent = new Angle(MathF.Atan2(normal.Y, normal.X)) + Angle.FromDegrees(90d);

            // An octant on an axis is a straight run of ring; the ones in between turn a corner.
            var octant = (int)MathF.Round((float)(tangent.Theta / (MathF.PI / 4f)));
            var proto = (octant & 1) == 0 ? RimStraightDecal : RimCurveDecal;

            if (!_decals.TryAddDecal(proto, new EntityCoordinates(groundMap, index), out var id, rotation: tangent, zIndex: 0, cleanable: false))
            {
                // The only tile-side refusal is a space tile; a cosmetically broken ring beside a chasm is not an error.
                refused++;
                continue;
            }

            _rimDecals.Add(id);
        }

        if (refused > 0)
            Log.Warning($"{refused} of {_rimIndices.Count} rim decals were refused on {ToPrettyString(groundMap)}; the ring is incomplete where the ground is space.");
    }

    /// <summary>
    /// Advisory clearance check over the berth: the extraction proceeds either way, but it says what it hit.
    /// The ignore set is handed in rather than built here, so the caller owns the set Smimsh later depends on.
    /// </summary>
    /// <summary>
    /// Where a disc of this radius actually hangs: the berth centre pushed out along the marker's facing until the disc
    /// clears the cracker and everything docked to it, with the marker's own Distance as the floor. The distance is
    /// a mapper's guess; the radius is half the pair spacing plus padding, 10 to 22 tiles across the pairing band, so
    /// a berth eight tiles off the deck hung the disc through the hull and the two grids' contacts welded them.
    /// </summary>
    public Vector2 GetBerthCentreClearOf(
        Entity<WFPlanetCrackerComponent> cracker,
        MapCoordinates berth,
        float radius,
        IReadOnlySet<EntityUid> hulls)
    {
        if (cracker.Comp.Berth is not { } netBerth || !TryGetEntity(netBerth, out var marker))
            return berth.Position;

        var (markerPos, markerRot) = _transform.GetWorldPositionRotation(marker.Value);
        var facing = markerRot.ToWorldVec();

        // The furthest any hull corner reaches past the marker along its facing; never behind it.
        var reach = 0f;

        foreach (var hull in hulls)
        {
            if (!TryComp<MapGridComponent>(hull, out var grid))
                continue;

            var matrix = _transform.GetWorldMatrix(hull);
            var box = grid.LocalAABB;

            reach = MathF.Max(reach, Vector2.Dot(Vector2.Transform(box.BottomLeft, matrix) - markerPos, facing));
            reach = MathF.Max(reach, Vector2.Dot(Vector2.Transform(box.BottomRight, matrix) - markerPos, facing));
            reach = MathF.Max(reach, Vector2.Dot(Vector2.Transform(box.TopLeft, matrix) - markerPos, facing));
            reach = MathF.Max(reach, Vector2.Dot(Vector2.Transform(box.TopRight, matrix) - markerPos, facing));
        }

        var distance = MathF.Max((berth.Position - markerPos).Length(), reach + radius + BerthClearance);
        return markerPos + facing * distance;
    }

    private void WarnOnObstruction(
        Entity<WFPlanetCrackerComponent> cracker,
        Entity<MapGridComponent> chunk,
        MapId berthMap,
        Vector2 target,
        IReadOnlySet<EntityUid> ignored)
    {
        _found.Clear();
        _mapManager.FindGridsIntersecting(berthMap, chunk.Comp.LocalAABB.Translated(target), ref _found, approx: false, includeMap: false);

        foreach (var found in _found)
        {
            if (found.Owner == chunk.Owner || ignored.Contains(found.Owner))
                continue;

            Log.Warning($"{ToPrettyString(found.Owner)} is inside {ToPrettyString(cracker.Owner)}'s chunk berth at extraction.");
        }
    }

    /// <summary>
    /// Parks the chunk: no atmosphere, its own inherent gravity, no roof, immune to cleanup and force-anchored by hand.
    /// ForceAnchorComponent is inert on a code-built grid - the grid is never map-initialised, so its handler never
    /// runs - so everything that handler would do is done here and the component is added last, as the marker only.
    /// </summary>
    private void ParkChunk(Entity<MapGridComponent> chunk)
    {
        var gravity = EnsureComp<GravityComponent>(chunk.Owner);
        gravity.Inherent = true;
        gravity.Enabled = true;
        Dirty(chunk.Owner, gravity);

        // The shuttle system adds this to every new grid and CE only strips it inside a z-grid network; left on, the
        // chunk roofs itself in black.
        RemComp<ImplicitRoofComponent>(chunk.Owner);
        EnsureComp<CleanupImmuneComponent>(chunk.Owner);

        if (!HasComp<ShuttleComponent>(chunk.Owner))
        {
            Log.Error($"{ToPrettyString(chunk.Owner)} has no ShuttleComponent after grid init; it could never have been released.");
            EnsureComp<ShuttleComponent>(chunk.Owner);
        }

        _shuttle.Disable(chunk.Owner, force: true);
        EnsureComp<PreventGridAnchorChangesComponent>(chunk.Owner);
        EnsureComp<ForceAnchorComponent>(chunk.Owner);

        // AFTER the disable, not before it: Disable pins the rotation, SetFixedRotation resets the body's mass data,
        // and AutomaticAtmosSystem hands any grid past seven tiles of mass a fresh GridAtmosphere on that very event.
        // Removing it any earlier in the parking order just gets it handed straight back (design D3).
        RemComp<GridAtmosphereComponent>(chunk.Owner);

        if (!TryComp<PhysicsComponent>(chunk.Owner, out var body) || body.BodyType != BodyType.Static)
            Log.Error($"{ToPrettyString(chunk.Owner)} is not a static body after parking; it will drift out of the berth.");
    }

    /// <summary>
    /// Marks the planet cracked so it can never be cut twice.
    /// The flag lives on the sector body rather than the z-network, because the network can be torn down and rebuilt
    /// while the body is what the sector survey console enumerates.
    /// </summary>
    private void FlagPlanet(Entity<WFPlanetCrackerComponent> cracker, EntityUid chunk, EntityUid orbitMap, EntityUid groundMap)
    {
        if (!_crackers.TryGetPlanetFromOrbit(orbitMap, out var planet) && !TryGetPlanetFromGround(groundMap, out planet))
        {
            Log.Error($"{ToPrettyString(cracker.Owner)} extracted on a stack with no sector body (orbit {ToPrettyString(orbitMap)}, ground {ToPrettyString(groundMap)}); this planet can be cracked again.");
            return;
        }

        planet.Comp.Cracked = true;
        Dirty(planet);

        var ev = new WFPlanetCrackedEvent(planet.Owner, chunk, cracker.Owner);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>The fallback resolution: the ground layer's z-network back-link to the sector body.</summary>
    private bool TryGetPlanetFromGround(EntityUid groundMap, out Entity<WFSectorPlanetComponent> planet)
    {
        planet = default;

        if (!TryComp<WFPlanetLayerComponent>(groundMap, out var layer))
            return false;

        if (layer.Network is not { } netNetwork || !TryGetEntity(netNetwork, out var network))
            return false;

        if (!TryComp<WFPlanetNetworkComponent>(network, out var networkComp) || networkComp.Planet is not { } body)
            return false;

        if (!TryComp<WFSectorPlanetComponent>(body, out var sector))
            return false;

        planet = (body, sector);
        return true;
    }
}
