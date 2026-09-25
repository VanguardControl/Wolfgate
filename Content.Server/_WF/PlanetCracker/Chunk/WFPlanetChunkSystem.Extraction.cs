using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._NF.Shuttles.Components;
using Content.Server._Mono.Cleanup;
using Content.Server._WF.Planets;
using Content.Server.Atmos.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.Planets;
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

/// <summary>The extraction: copy the disc to a new grid, move its riders, empty and pin the hole, park it.</summary>
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

    /// <summary>The one definition of "inside the circle"; the tile copy, entity move and pin all read it.</summary>
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

    /// <summary>Cuts the disc free and hangs it in the hull's berth; the step order matters.</summary>
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

        // STEP 1: force the terrain to exist, one tile wider so the rim is generated in the same pass.
        _tileBuffer.Clear();
        _biome.ReserveTiles(groundMap, aabb.Enlarged(1f), _tileBuffer, mapGrid: groundGrid);

        // STEP 2: read the disc by tile centre; ignoreEmpty: false, as empty tiles in the circle must be pinned too.
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

        // STEP 3: create on the ground map; a grid created on the orbit map gets no CEZPhysics or faller.
        var chunkEnt = _mapManager.CreateGridEntity(groundMapId);
        _transform.SetWorldPositionRotation(chunkEnt.Owner, groundWorldPos, groundWorldRot);

        // Before the first SetTiles: a disc with a lake or chasm isn't simply connected and would split.
        chunkEnt.Comp.CanSplit = false;

        // Also before SetTiles: tiles far off-origin give a negative inertia that asserts; fixed rotation skips it.
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

        // STEP 8: pin the hole and the rim; the pin is all that stops the biome regenerating the hole.
        _biome.WfPinTiles(biome, _holeIndices);

        BuildRim(groundMap, groundGrid, centre, radius);
        _biome.WfPinTiles(biome, _rimIndices);

        // STEP 9: the decal ring, after the stamp, on pinned biome ground just outside the circle.
        StampRim(groundMap, centre, half);

        // Built before the berth pose: the pose and Smimsh both skip this set, or they'd crush the cutting hull.
        _ignored.Clear();
        _shuttle.GetAllDockedShuttles(cracker.Owner, _ignored);

        // STEP 10: hang it in the berth, turned about its disc centre (taken to ground-local) to the hull's heading.
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

        // STEP 11: clear the berth of loose props, ignoring the cracker and its docked set.
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

        // Planets treats it as ground, not a hull: no flight, drag, orbit decay, infestation or biomass.
        EnsureComp<WFDetachedTerrainComponent>(chunkEnt.Owner);

        cracker.Comp.Chunk = GetNetEntity(chunkEnt.Owner);
        Dirty(cracker);

        LayGangway(cracker, (chunkEnt.Owner, comp), hang, radius, _ignored);

        // The connector never subscribes MoveEvent, so a new grid on a layer asks for the recalculation itself.
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

    /// <summary>Moves everything inside the circle onto the chunk, by tile index; the lookup only prefilters.</summary>
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

            // Checked before the unanchor, which can't be undone: an empty chunk tile refuses an anchored rider.
            if (xform.Anchored && _map.GetTileRef(chunk.Owner, chunk.Comp, index).Tile.IsEmpty)
            {
                Log.Error($"Chunk tile {index} is empty, so anchored {ToPrettyString(uid)} could not ride up; it was left anchored on the ground map.");
                continue;
            }

            // An anchor's unanchor/re-anchor would dissolve its pair; suppress its handler for the move.
            var isAnchor = HasComp<WFGravityAnchorComponent>(uid);

            if (isAnchor)
                _anchors.BeginChunkRide(uid);

            if (xform.Anchored)
            {
                _transform.Unanchor(uid, xform, setPhysics: false);

                // The cross-grid overload; false means an empty destination tile, which the disc filter rules out.
                if (!_transform.AnchorEntity((uid, xform), (chunk.Owner, chunk.Comp), index))
                {
                    Log.Error($"Could not anchor {ToPrettyString(uid)} onto chunk tile {index}; it was left on the ground map.");

                    // Put it back; a detached but Static rider is worse than an unmoved one.
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

    /// <summary>Stamps the rim ring; decals anchor bottom-left and rotate by angle, as only frame 0 is drawn.</summary>
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

    /// <summary>The berth centre pushed along the marker's facing until a disc this size clears every hull.</summary>
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

    /// <summary>Advisory berth clearance check: logs what it hits; the extraction goes ahead anyway.</summary>
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

    /// <summary>Parks the chunk: no atmos or roof, own gravity, cleanup-immune and force-anchored by hand.</summary>
    private void ParkChunk(Entity<MapGridComponent> chunk)
    {
        var gravity = EnsureComp<GravityComponent>(chunk.Owner);
        gravity.Inherent = true;
        gravity.Enabled = true;
        Dirty(chunk.Owner, gravity);

        // Added to every new grid; left on, the chunk roofs itself in black.
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

        // After the disable, whose mass reset makes AutomaticAtmosSystem hand the atmosphere straight back.
        RemComp<GridAtmosphereComponent>(chunk.Owner);

        if (!TryComp<PhysicsComponent>(chunk.Owner, out var body) || body.BodyType != BodyType.Static)
            Log.Error($"{ToPrettyString(chunk.Owner)} is not a static body after parking; it will drift out of the berth.");
    }

    /// <summary>Marks the sector body cracked so the planet can never be cut twice.</summary>
    private void FlagPlanet(Entity<WFPlanetCrackerComponent> cracker, EntityUid chunk, EntityUid orbitMap, EntityUid groundMap)
    {
        if (!_crackers.TryGetPlanetFromOrbit(orbitMap, out var planet) && !TryGetPlanetFromGround(groundMap, out planet))
        {
            Log.Error($"{ToPrettyString(cracker.Owner)} extracted on a stack with no sector body (orbit {ToPrettyString(orbitMap)}, ground {ToPrettyString(groundMap)}); this planet can be cracked again.");
            return;
        }

        EnsureComp<WFPlanetCrackedComponent>(planet.Owner);

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
