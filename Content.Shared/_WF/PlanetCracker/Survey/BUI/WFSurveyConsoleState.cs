using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Survey.BUI;

/// <summary>
/// Everything the sector survey console's window draws, authored server-side in one pass.
/// The point is CONSISTENCY, not PVS: the sector bodies themselves are already on every client - StarSystemMapSystem
/// calls _pvs.AddGlobalOverride on each spawned body (Content.Server/_FarHorizons/StarSystem/StarSystemMapSystem.cs:65)
/// and WFSectorPlanetComponent is networked - so the only genuinely new information here is the vein rating. Shipping
/// the whole snapshot means the window never joins an entity query to a prototype index client-side, and every row is
/// internally consistent with every other row and with the distance they were all measured from.
/// The console is read-only: there is no matching messages file, because the "go here" is each body's own pre-existing
/// FTL beacon name and there is nothing for the client to ask the server to do.
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
/// One star-system body as the survey console shows it.
/// Beacon is the body's own MetaData name - exactly the label ShuttleConsoleSystem.GetBeacons puts in the shuttle
/// console's destination tree (Content.Server/Shuttles/Systems/ShuttleConsoleSystem.FTL.cs:99-108) - because every
/// sector body already ships FTLBeaconComponent
/// (Resources/Prototypes/_FarHorizons/Entities/Objects/StarSystem/star_system.yml:20). HasBeacon is the regression
/// guard for that: it goes false the day a body stops carrying the component, instead of the row quietly naming a
/// destination that is no longer in the pilot's list.
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
