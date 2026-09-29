using Content.Server.Atmos.Components;
using Content.Shared.Atmos;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>A copy of the air on each of a grid's own tiles, by tile index.</summary>
    public Dictionary<Vector2i, GasMixture> WfCopyGridAir(EntityUid grid)
    {
        var air = new Dictionary<Vector2i, GasMixture>();
        if (!TryComp<GridAtmosphereComponent>(grid, out var atmos))
            return air;

        foreach (var (index, tile) in atmos.Tiles)
        {
            if (!tile.NoGridTile && !tile.MapAtmosphere && tile.Air is { Immutable: false } mixture)
                air[index] = mixture.Clone();
        }

        return air;
    }

    /// <summary>Gives tiles just added to a grid their air, before revalidation would start them as vacuum.</summary>
    public void WfSeedGridAir(EntityUid grid, Dictionary<Vector2i, GasMixture> air)
    {
        if (air.Count == 0 || !TryComp<GridAtmosphereComponent>(grid, out var atmos))
            return;

        foreach (var (index, mixture) in air)
        {
            var tile = GetOrNewTile(grid, atmos, index);
            if (tile.MapAtmosphere)
                RemoveMapAtmos(atmos, tile);

            tile.Air = mixture;
            atmos.InvalidatedCoords.Add(index);
        }
    }
}
