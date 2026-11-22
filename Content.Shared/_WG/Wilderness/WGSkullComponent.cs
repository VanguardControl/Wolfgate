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

    /// <summary>
    /// A standing mark staff set: it never runs out, and ends only when staff clear it, a person dies or a ship is
    /// destroyed. <see cref="ExpiresAt"/> stays null.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Staff;

    /// <summary>
    /// A mark the character carries for life, from <see cref="WGMarkedTraitComponent"/>: it never runs out and outlasts
    /// death. Only staff can clear it. <see cref="ExpiresAt"/> stays null.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Permanent;

    /// <summary>Whether the mark never runs out on its own: a standing or a permanent one.</summary>
    public bool Lasting => Staff || Permanent;
}

/// <summary>A ship or person in a Wilderness and not yet marked: when they came in.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WGWildernessDwellComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Since;
}
