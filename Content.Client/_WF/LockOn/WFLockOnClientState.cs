namespace Content.Client._WF.LockOn;

/// <summary>
/// The weapons the cockpit is currently controlling, so the lock overlay on the shared helm radar can draw
/// their lead points. Null while no cockpit is open.
/// </summary>
public static class WFLockOnClientState
{
    /// <summary>Returns the linked gunnery console's selected guns, or an empty list.</summary>
    public static Func<IReadOnlyList<NetEntity>>? CockpitWeapons;
}
