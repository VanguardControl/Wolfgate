using Content.Shared._CE.ZLevels.Core.Components;

namespace Content.Shared._CE.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    /// <summary>Whether a body that just moved down a level came onto ground right under it, such as stairs, and so did not fall.</summary>
    // Read after the move, when the ground height is the new level's.
    private static bool WfSteppedDown(CEZPhysicsComponent body)
    {
        return body.LocalPosition - body.CachedGroundHeight <= AirborneHeightThreshold;
    }
}
