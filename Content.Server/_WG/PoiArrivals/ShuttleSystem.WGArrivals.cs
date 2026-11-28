using Robust.Shared.Map;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>A spot near the target, clear of other grids, for the shuttle to arrive at.</summary>
    public bool WgTryGetFTLProximity(EntityUid shuttle, EntityCoordinates target, out EntityCoordinates coordinates, out Angle angle)
    {
        return TryGetFTLProximity(shuttle, target, out coordinates, out angle);
    }
}
