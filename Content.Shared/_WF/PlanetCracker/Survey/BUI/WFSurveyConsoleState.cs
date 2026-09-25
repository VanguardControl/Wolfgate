using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Survey.BUI;

/// <summary>
/// Everything the read-only sector survey console draws, built server-side in one pass so every row is consistent.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFSurveyConsoleState : BoundUserInterfaceState
{
    /// <summary>One row per body in the star system, registered or not, sorted by distance when it is known.</summary>
    public List<WFSurveyPlanetRow> Planets = new();

    /// <summary>Display name of the star system this console is reading.</summary>
    public string SystemName = string.Empty;

    /// <summary>False when the console's own sector position could not be resolved; every row's distance is then unknown.</summary>
    public bool ConsolePositionKnown;
}

/// <summary>
/// One star-system body as the survey console shows it; Beacon is the name the shuttle console's FTL list uses.
/// </summary>
[Serializable, NetSerializable]
public record struct WFSurveyPlanetRow(
    NetEntity? Planet,
    string Name,
    string Beacon,
    bool HasBeacon,
    Vector2 SectorPos,
    float Distance,
    bool DistanceKnown,
    bool HasSurface,
    bool Sanctioned,
    bool Cracked,
    WFVeinRating Veins,
    bool VeinsKnown);
