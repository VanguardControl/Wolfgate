using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// Sprite layers of a deep vein; Marker ships hidden and the client shows it for revealed veins.
/// </summary>
[Serializable, NetSerializable]
public enum WFDeepVeinVisualLayers : byte
{
    /// <summary>The vein glyph itself, hidden until this player has pulsed it.</summary>
    Marker,
}
