using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._NF.GameRule;

public sealed partial class PointOfInterestSystem
{
    /// <summary>
    /// Sets up a point of interest as a station on a holding map and picks where in the sector it goes, clear of the
    /// stations placed so far.
    /// </summary>
    public bool WgSpawnArrival(MapId holding, PointOfInterestPrototype proto, out EntityUid grid, out Vector2 destination)
    {
        grid = EntityUid.Invalid;
        var offset = GetRandomPOICoord(proto.MinimumDistance, proto.MaximumDistance);
        destination = new Vector2(offset.X + proto.PositionX, offset.Y + proto.PositionY);

        if (!TrySpawnPoiGrid(holding, proto, Vector2.Zero, out var spawned) || spawned is not { Valid: true } uid)
            return false;

        grid = uid;
        AddStationCoordsToSet(destination);
        return true;
    }
}
