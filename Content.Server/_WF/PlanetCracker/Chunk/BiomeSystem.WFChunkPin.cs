using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server.Parallax;

/// <summary>
/// The chunk extraction's only legal route into BiomeComponent: the component is [Access(typeof(SharedBiomeSystem))]
/// and both the chunk loader and the pin set are private, so a partial of this class is the one writer F5 may be.
/// Declares no subscriptions and re-declares no dependency.
/// </summary>
public sealed partial class BiomeSystem
{
    /// <summary>Tile accumulator for <see cref="WfUnloadChunk"/>; the private unloader writes into the caller's list.</summary>
    private readonly List<(Vector2i, Tile)> _wfUnloadBuffer = new();

    /// <summary>
    /// Pins tiles so the biome never regenerates or unloads them again: the hole stays a hole and the rim decal ring
    /// keeps its ground. ForEachTileInChunk skips anything in this set, which is what makes the pin stick.
    /// </summary>
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

    /// <summary>
    /// Forgets a biome-spawned entity that has been moved off this grid, and pins the tile it vacated.
    /// Without it the entry leaks: OnEntityTerminating can no longer find the entity once the chunk unloads, and the
    /// tile it stood on would be regenerated with a fresh copy of the prop.
    /// </summary>
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

    /// <summary>
    /// Loads one biome chunk by hand, with the production caller's own guard.
    /// LoadChunk is NOT idempotent - LoadEntities and LoadDecals both Add into a dictionary keyed by chunk and throw on
    /// a second call - so the LoadedChunks set has to be claimed first, exactly as LoadChunks does.
    /// </summary>
    public bool WfLoadChunk(Entity<BiomeComponent, MapGridComponent> biome, Vector2i chunkOrigin)
    {
        if (!biome.Comp1.LoadedChunks.Add(chunkOrigin))
            return false;

        LoadChunk(biome.Comp1, biome.Owner, biome.Comp2, chunkOrigin, biome.Comp1.Seed);
        return true;
    }

    /// <summary>
    /// Unloads one biome chunk by hand. This is the destructive half: UnloadTiles empties every UNPINNED index and the
    /// decal system then wipes the decals on it, so an unload is the only proof a pin actually holds.
    /// </summary>
    public bool WfUnloadChunk(Entity<BiomeComponent, MapGridComponent> biome, Vector2i chunkOrigin)
    {
        if (!biome.Comp1.LoadedChunks.Contains(chunkOrigin))
            return false;

        _wfUnloadBuffer.Clear();
        UnloadChunk(biome.Comp1, biome.Owner, biome.Comp2, chunkOrigin, biome.Comp1.Seed, _wfUnloadBuffer);
        return true;
    }
}
