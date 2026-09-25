using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// Handheld deep-vein surveyor: a short DoAfter that reveals every vein within <see cref="PulseRadius"/> to the user alone.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFSurveyorComponent : Component
{
    /// <summary>Reveal radius in tiles; keep inside net.pvs_range, as veins outside PVS are not on the client.</summary>
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
