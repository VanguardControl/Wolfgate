using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>Appearance keys the crack console writes, driving its GenericVisualizer screen layer.</summary>
[Serializable, NetSerializable]
public enum WFCrackConsoleVisuals : byte
{
    /// <summary>The console's current <see cref="WFCrackConsoleScreen"/>.</summary>
    Screen,
}

/// <summary>Screen faces crack_console.rsi already ships, one per broad phase of the crack.</summary>
[Serializable, NetSerializable]
public enum WFCrackConsoleScreen : byte
{
    /// <summary>Nothing under way.</summary>
    Idle,

    /// <summary>A pair is locked and targetable.</summary>
    Targeting,

    /// <summary>The cut is running.</summary>
    Cracking,

    /// <summary>The cut is done, or something is failing.</summary>
    Alert,
}
