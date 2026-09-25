using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server.Parallax;

/// <summary>Chunk extraction's access to BiomeComponent's loaded-entity bookkeeping.</summary>
public sealed partial class BiomeSystem
{
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
}
