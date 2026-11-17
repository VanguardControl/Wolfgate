using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.LockOn;

/// <summary>
/// Asks the console's ship to lock whatever sits under a radar click, or to clear the lock when the click found
/// nothing. Sent by the helm and gunnery consoles alike.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFLockOnRequestMessage(NetCoordinates coordinates) : BoundUserInterfaceMessage
{
    /// <summary>The click, in the map the console's radar plotted.</summary>
    public NetCoordinates Coordinates = coordinates;
}
