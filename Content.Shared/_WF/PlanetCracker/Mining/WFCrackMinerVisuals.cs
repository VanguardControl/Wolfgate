using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Mining;

/// <summary>Appearance keys the crack miner's GenericVisualizer table is written against.</summary>
[Serializable, NetSerializable]
public enum WFCrackMinerVisuals : byte
{
    /// <summary>The miner's current <see cref="WFCrackMinerState"/>.</summary>
    State,
}

/// <summary>Sprite layers mining.yml maps, in draw order.</summary>
[Serializable, NetSerializable]
public enum WFCrackMinerVisualLayers : byte
{
    /// <summary>The body of the miner.</summary>
    Base,

    /// <summary>Unshaded mining glow.</summary>
    Glow,
}
