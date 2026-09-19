using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>What the gravity projector is doing, for the sprite and for F4's grace timer.</summary>
[Serializable, NetSerializable]
public enum WFProjectorState : byte
{
    /// <summary>Unpowered.</summary>
    Off,

    /// <summary>Powered and waiting for a target.</summary>
    Idle,

    /// <summary>Spinning up on a target pair.</summary>
    Charging,

    /// <summary>Cutting.</summary>
    Firing,

    /// <summary>Past its Breakage threshold and needs repair.</summary>
    Broken,
}

/// <summary>Appearance keys the projector writes.</summary>
[Serializable, NetSerializable]
public enum WFProjectorVisuals : byte
{
    /// <summary>The projector's current <see cref="WFProjectorState"/>.</summary>
    State,
}

/// <summary>Sprite layers of the gravity projector.</summary>
[Serializable, NetSerializable]
public enum WFProjectorVisualLayers : byte
{
    /// <summary>The machine body.</summary>
    Base,

    /// <summary>Unshaded emitter glow.</summary>
    Emitter,
}
