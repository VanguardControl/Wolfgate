using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// The handheld deep-vein surveyor: used in hand it runs a short DoAfter and, on the server, reveals every vein inside
/// <see cref="PulseRadius"/> to the user alone. It deliberately carries no ItemToggleComponent (see the shared system)
/// and no appearance data of any kind - F2 ships no scanning face.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFSurveyorComponent : Component
{
    /// <summary>
    /// Reveal radius in tiles. Deliberately well inside net.pvs_range (25 tiles,
    /// RobustToolbox/Robust.Shared/CVars.cs:232) because veins carry no PVS override: a vein outside PVS is not on the
    /// client at all, so revealing it would draw nothing.
    /// </summary>
    [DataField]
    public float PulseRadius = 12f;

    /// <summary>How long the scan DoAfter runs.</summary>
    [DataField]
    public TimeSpan ScanDuration = TimeSpan.FromSeconds(2);

    /// <summary>Cosmetic effect spawned at the user on a completed scan; server-side only.</summary>
    [DataField]
    public EntProtoId PulseEffect = "WFEffectSurveyPulse";

    /// <summary>Ping played on a completed scan.</summary>
    [DataField]
    public SoundSpecifier PulseSound = new SoundPathSpecifier("/Audio/Machines/sonar-ping.ogg");
}
