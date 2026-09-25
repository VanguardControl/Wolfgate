using System.Numerics;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Fissures;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._WF.PlanetCracker.Fissures;

/// <summary>One ring of fissures: the annulus, tile filter, biome pin, decal stamp and its growth stages.</summary>
public sealed partial class WFFissureSpawnerSystem
{
    /// <summary>Tint for the stamped cracks: the borrowed art is near-white glass, the ground wants dark soil.</summary>
    private static readonly Color FissureColour = Color.FromHex("#2b2118");

    /// <summary>The four growth stages in order; SetDecalId throws on an unknown id, so only these reach it.</summary>
    private static readonly string[] FissureDecals = { "WFFissure1", "WFFissure2", "WFFissure3", "WFFissure4" };

    /// <summary>Accumulator BiomeSystem.ReserveTiles fills; cleared before every call because ReserveTiles does not.</summary>
    private readonly List<(Vector2i Index, Tile Tile)> _tileBuffer = new();

    /// <summary>Every tile index of the band currently being considered, shuffled in place.</summary>
    private readonly List<Vector2i> _ringBuffer = new();

    /// <summary>The indices this ring or surge actually took, which is also exactly the set that gets pinned.</summary>
    private readonly List<Vector2i> _chosen = new();

    /// <summary>Lookup result of the stamp-by-lookup pass; cleared before every spawn.</summary>
    private readonly HashSet<EntityUid> _lookupBuffer = new();

    /// <summary>The same lookup taken before a spawn, so only what that spawn created is stamped.</summary>
    private readonly HashSet<EntityUid> _preSpawnBuffer = new();

    /// <summary>Spreads one ring: picks its tiles, pins them, grows the older decals and stamps the new ones.</summary>
    private void SpreadRing(Entity<WFFissureSpawnerComponent> ent, WFGravityAnchorComponent anchor)
    {
        var comp = ent.Comp;

        if (comp.Ground is not { } groundUid ||
            !TryComp<MapGridComponent>(groundUid, out var mapGrid) ||
            !TryComp<BiomeComponent>(groundUid, out var biome))
        {
            comp.Armed = false;
            return;
        }

        var ground = new Entity<MapGridComponent>(groundUid, mapGrid);
        var radius = comp.FirstRingRadius + comp.RingsDone * comp.RingStep;

        // The bands ((r-0.5)^2, (r+0.5)^2] of successive rings are disjoint, so no tile is offered twice.
        BuildBand(ground, comp.Centre, radius - 0.5f, radius + 0.5f);
        _random.Shuffle(_ringBuffer);

        var want = _random.Next(comp.MinFissures, comp.MaxFissures + 1);

        if (!comp.Sanctioned)
            want = (int)MathF.Ceiling(want * comp.UnsanctionedMultiplier);

        // The ground layer's grid IS its map entity, so the anchor's world position is already a grid-local one.
        var origin = _map.TileIndicesFor(ground, new EntityCoordinates(ground.Owner, comp.Centre));

        _chosen.Clear();

        foreach (var index in _ringBuffer)
        {
            if (_chosen.Count >= want)
                break;

            // Skip the anchor's own footprint; the partner is far enough away not to need a check.
            if (Math.Max(Math.Abs(index.X - origin.X), Math.Abs(index.Y - origin.Y)) <= anchor.FootprintRadius)
                continue;

            if (!EnsureTile(ground, index))
                continue;

            // The salvage expedition's spawn gate.
            if (!_anchorable.TileFree(ground, index, (int)CollisionGroup.MachineLayer, (int)CollisionGroup.MachineLayer))
                continue;

            _chosen.Add(index);
        }

        if (_chosen.Count == 0)
            return;

        // Without the pin a chunk unload empties the tiles and deletes their decals.
        _biome.WfPinTiles((ground.Owner, biome), _chosen);

        PromoteDecals(ground.Owner, comp);
        StampFissures(ground, comp, comp.Centre, 1);

        // One sound per ring, not one per tile.
        _audio.PlayPvs(comp.CrackSound, new EntityCoordinates(ground.Owner, comp.Centre));

        SpawnRingMobs(ent, ground, ent.Owner, _chosen);
    }

    /// <summary>Raises every decal this anchor has stamped one growth stage, capped at stage four.</summary>
    private void PromoteDecals(EntityUid ground, WFFissureSpawnerComponent comp)
    {
        var count = Math.Min(comp.Decals.Count, comp.DecalStages.Count);

        for (var i = 0; i < count; i++)
        {
            var stage = comp.DecalStages[i];

            if (stage >= FissureDecals.Length)
                continue;

            // FissureDecals is indexed from zero and the stage from one, and only these four ids may be passed.
            if (!_decals.SetDecalId(ground, comp.Decals[i], FissureDecals[stage]))
                continue;

            comp.DecalStages[i] = (byte)(stage + 1);
        }
    }

    /// <summary>One decal and one burst per chosen index, in one pass so the ring is a single chunk send.</summary>
    private void StampFissures(Entity<MapGridComponent> ground, WFFissureSpawnerComponent comp, Vector2 centre, byte stage)
    {
        var half = ground.Comp.TileSizeHalfVector;
        var refused = 0;

        foreach (var index in _chosen)
        {
            // Decals sit at their bottom-left, so the tile index; rotation via angle, as the overlay draws frame zero.
            if (_decals.TryAddDecal(
                    FissureDecals[stage - 1],
                    new EntityCoordinates(ground.Owner, index),
                    out var id,
                    color: FissureColour,
                    rotation: RingTangent((Vector2)index + half - centre),
                    zIndex: 0,
                    cleanable: false))
            {
                comp.Decals.Add(id);
                comp.DecalStages.Add(stage);
                comp.Fissures.Add(index);
            }
            else
            {
                refused++;
            }

            // Entities sit at their centre, unlike decals.
            Spawn(comp.BurstEffect, new EntityCoordinates(ground.Owner, (Vector2)index + half));
        }

        // Only space tiles refuse; an incomplete ring beside a chasm is not an error.
        if (refused > 0)
            Log.Warning($"{refused} of {_chosen.Count} fissure decals were refused on {ToPrettyString(ground.Owner)}; the ring is incomplete where the ground is space.");
    }

    /// <summary>Every tile index whose centre falls in the band (inner, outer], collected into the ring buffer.</summary>
    private void BuildBand(Entity<MapGridComponent> ground, Vector2 centre, float inner, float outer)
    {
        _ringBuffer.Clear();

        var half = ground.Comp.TileSizeHalfVector;
        var innerSq = inner * inner;
        var outerSq = outer * outer;
        var bounds = Box2.CenteredAround(centre, new Vector2(outer * 2f, outer * 2f)).Enlarged(1f);

        var tiles = _map.GetLocalTilesEnumerator(ground.Owner, ground.Comp, bounds, ignoreEmpty: false);

        while (tiles.MoveNext(out var tileRef))
        {
            var distanceSq = ((Vector2)tileRef.GridIndices + half - centre).LengthSquared();

            if (distanceSq <= innerSq || distanceSq > outerSq)
                continue;

            _ringBuffer.Add(tileRef.GridIndices);
        }
    }

    /// <summary>Makes sure one index has real ground, a tile at a time as ReserveTiles pins its whole box.</summary>
    private bool EnsureTile(Entity<MapGridComponent> ground, Vector2i index)
    {
        if (_map.TryGetTileRef(ground.Owner, ground.Comp, index, out var tile) && !tile.Tile.IsEmpty)
            return true;

        _tileBuffer.Clear();
        _biome.ReserveTiles(
            ground.Owner,
            new Box2(index.X, index.Y, index.X + 1, index.Y + 1),
            _tileBuffer,
            mapGrid: ground.Comp);

        return _map.TryGetTileRef(ground.Owner, ground.Comp, index, out tile) && !tile.Tile.IsEmpty;
    }

    /// <summary>The outward normal's angle snapped to the nearest quarter turn, so a fissure lies along the ring.</summary>
    private static Angle RingTangent(Vector2 normal)
    {
        if (normal.LengthSquared() <= 0f)
            return Angle.Zero;

        const float quarter = MathF.PI / 2f;

        return new Angle(MathF.Round(MathF.Atan2(normal.Y, normal.X) / quarter) * quarter);
    }
}
