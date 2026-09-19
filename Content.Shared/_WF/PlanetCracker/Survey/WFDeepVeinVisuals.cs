using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// Sprite layers of a deep vein. The prototype ships Marker with visible: false; the client visuals system flips it on
/// for revealed veins and the overlay draws the same layer a second time with the ping-fade alpha.
/// </summary>
[Serializable, NetSerializable]
public enum WFDeepVeinVisualLayers : byte
{
    /// <summary>The vein glyph itself, hidden until this player has pulsed it.</summary>
    Marker,
}
