using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.Wilderness;

/// <summary>
/// A ship or person marked under the Wilderness rules, each carrying its own: exempt from the rules' protections, Safe
/// Zone included, until the mark runs out. Seen by everyone, on radar and over a person's head.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WGSkullComponent : Component
{
    /// <summary>When the mark runs out; null while still in a Wilderness, where it is held at full.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan? ExpiresAt;
}

/// <summary>A ship or person in a Wilderness and not yet marked: when they came in.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WGWildernessDwellComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Since;
}
