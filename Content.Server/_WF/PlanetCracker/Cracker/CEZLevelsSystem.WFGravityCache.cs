namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>
    /// Forces the next gravity sweep to rebuild the pooled gravgen capacity and rigid-support caches instead of
    /// waiting out the half-second throttle. A grid whose lift was just revoked (the crack fall) would otherwise read
    /// as supported for up to that long and settle straight back onto the layer it was pushed off.
    /// </summary>
    public void WfInvalidateGravgenCapacity()
    {
        _nextGravityCheckTime = TimeSpan.Zero;
    }
}
