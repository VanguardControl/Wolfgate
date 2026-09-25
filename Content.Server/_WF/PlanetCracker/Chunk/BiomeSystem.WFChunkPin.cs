using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server.Parallax;

/// <summary>Chunk extraction's access to BiomeComponent's private chunk loader and pin set.</summary>
public sealed partial class BiomeSystem
{
    /// <summary>Tile accumulator for <see cref="WfUnloadChunk"/>; the private unloader writes into the caller's list.</summary>
    private readonly List<(Vector2i, Tile)> _wfUnloadBuffer = new();

    /// <summary>Pins tiles so the biome never regenerates or unloads them again.</summary>
    public void WfPinTiles(Entity<BiomeComponent> biome, IReadOnlyCollection<Vector2i> indices)
    {
        foreach (var index in indices)
        {
            var chunkOrigin = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;
            biome.Comp.ModifiedTiles.GetOrNew(chunkOrigin).Add(index);
        }
    }

    /// <summary>Whether one tile index is pinned against biome regeneration.</summary>
    public bool WfIsPinned(Entity<BiomeComponent> biome, Vector2i index)
    {
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;

        return biome.Comp.ModifiedTiles.TryGetValue(chunkOrigin, out var modified) && modified.Contains(index);
    }

    /// <summary>Forgets a biome entity moved off this grid and pins its tile so the prop isn't regenerated.</summary>
    public void WfForgetLoadedEntity(Entity<BiomeComponent> biome, EntityUid moved, Vector2i index)
    {
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;

        if (biome.Comp.LoadedEntities.TryGetValue(chunkOrigin, out var loaded) && loaded.Remove(moved))
        {
            biome.Comp.ModifiedTiles.GetOrNew(chunkOrigin).Add(index);
            return;
        }

        // The entity may have drifted out of the chunk it was spawned in, exactly as OnEntityTerminating allows for.
        foreach (var (origin, entities) in biome.Comp.LoadedEntities)
        {
            if (!entities.Remove(moved, out var storedTile))
                continue;

            biome.Comp.ModifiedTiles.GetOrNew(origin).Add(storedTile);
            break;
        }

        biome.Comp.ModifiedTiles.GetOrNew(chunkOrigin).Add(index);
    }

    /// <summary>Loads one biome chunk by hand; claims LoadedChunks first as LoadChunk throws on a repeat.</summary>
    public bool WfLoadChunk(Entity<BiomeComponent, MapGridComponent> biome, Vector2i chunkOrigin)
    {
        if (!biome.Comp1.LoadedChunks.Add(chunkOrigin))
            return false;

        LoadChunk(biome.Comp1, biome.Owner, biome.Comp2, chunkOrigin, biome.Comp1.Seed);
        return true;
    }

    /// <summary>Unloads one biome chunk by hand, emptying every unpinned tile and its decals.</summary>
    public bool WfUnloadChunk(Entity<BiomeComponent, MapGridComponent> biome, Vector2i chunkOrigin)
    {
        if (!biome.Comp1.LoadedChunks.Contains(chunkOrigin))
            return false;

        _wfUnloadBuffer.Clear();
        UnloadChunk(biome.Comp1, biome.Owner, biome.Comp2, chunkOrigin, biome.Comp1.Seed, _wfUnloadBuffer);
        return true;
    }
}
