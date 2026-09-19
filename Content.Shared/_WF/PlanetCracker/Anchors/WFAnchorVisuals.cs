using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Anchors;

/// <summary>Appearance keys the anchor writes; driven from YAML GenericVisualizer.</summary>
[Serializable, NetSerializable]
public enum WFAnchorVisuals : byte
{
    /// <summary>The anchor's current <see cref="WFAnchorState"/>.</summary>
    State,

    /// <summary>Whether the anchor is past its damage threshold.</summary>
    Damaged,
}

/// <summary>Sprite layers of the gravity anchor.</summary>
[Serializable, NetSerializable]
public enum WFAnchorVisualLayers : byte
{
    /// <summary>The body of the rig.</summary>
    Base,

    /// <summary>Unshaded drill/lock glow.</summary>
    Glow,

    /// <summary>Damage overlay.</summary>
    Damage,
}

/// <summary>Appearance keys the anchor crate writes.</summary>
[Serializable, NetSerializable]
public enum WFCrateVisuals : byte
{
    /// <summary>Whether the crate has been pried open.</summary>
    Open,
}

/// <summary>Sprite layers of the anchor crate.</summary>
[Serializable, NetSerializable]
public enum WFCrateVisualLayers : byte
{
    /// <summary>The crate body.</summary>
    Base,

    /// <summary>Variant stencil marking, such as the replacement crate.</summary>
    Stencil,
}
