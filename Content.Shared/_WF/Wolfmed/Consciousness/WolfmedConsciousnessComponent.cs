using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>How awake a wound host is. Replaces the damage thresholds' Alive/Critical decision (CONSC).</summary>
public enum WolfmedConsciousness : byte
{
    /// <summary>On their feet, no restrictions.</summary>
    Up = 0,

    /// <summary>Conscious but on the floor: can crawl, talk and use items on themselves only.</summary>
    Downed = 1,

    /// <summary>MobState.Critical. Nothing.</summary>
    Unconscious = 2,
}

/// <summary>
/// Consciousness of a wound host. Ensured on every wound host at startup; the server owns State and Depth,
/// the client reads them for the Downed alert and the dying view.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedConsciousnessComponent : Component
{
    [AutoNetworkedField]
    public WolfmedConsciousness State = WolfmedConsciousness.Up;

    /// <summary>
    /// How far gone the body is, 0 to 1. What the dying view draws instead of a damage ratio: 0.35 to 0.55
    /// across Downed, 0.55 to 1 across Unconscious.
    /// </summary>
    [AutoNetworkedField]
    public float Depth;

    /// <summary>
    /// Levels pushed in from outside the pain, blood and leg inputs; 0 none, 1 unconscious. The seam BRAIN
    /// pushes brain oxygenation through, and where airloss sits until it does.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, float> Pressures = new();

    /// <summary>
    /// BRAIN: brain oxygenation, 1 down to 0. Mirrored off the brain organ so the client can fade the view
    /// out as the clock runs without the organ entity being in its PVS.
    /// </summary>
    [AutoNetworkedField]
    public float Oxygenation = 1f;

    /// <summary>Blood volume fraction at the bloodstream's last tick. 1 when the body has no bloodstream.</summary>
    [ViewVariables]
    public float BloodFraction = 1f;

    /// <summary>Server: whether the periodic re-evaluation still has anything to watch on this body.</summary>
    [ViewVariables]
    public bool Watching;

    [ViewVariables]
    public TimeSpan NextEvaluation;

    /// <summary>Server: how far past its entry value each state's worst input was, for the hysteresis test.</summary>
    [ViewVariables]
    public float DownLevel;

    [ViewVariables]
    public float OutLevel;

    /// <summary>
    /// Server: the earliest the body may stand back up. A stun landing on the Downed edge used to flip the
    /// state several times a second, and every flip was another body-fall sound.
    /// </summary>
    [ViewVariables]
    public TimeSpan DownedUntil;

    /// <summary>
    /// Server: the body has been on its feet at least once. A body still being assembled has no legs yet,
    /// which reads as both legs gone, and the dwell must not hold a crewman down before they ever stood up.
    /// </summary>
    [ViewVariables]
    public bool WasUp;

    /// <summary>
    /// Server: the body has had a working leg at least once. Until then a missing pair of legs is a body
    /// still being assembled, and downing it played the body-fall sound on every spawn.
    /// </summary>
    [ViewVariables]
    public bool HadLegs;
}

/// <summary>
/// On the floor and restricted to themselves. A separate component so the action blockers subscribe to it
/// rather than testing consciousness on every attempt event.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedDownedComponent : Component
{
    /// <summary>Do-afters take this much longer while Downed: a medipen is slower flat on your back.</summary>
    [DataField]
    public float DoAfterMultiplier = 1.5f;

    /// <summary>The alert shown for as long as the body is Downed.</summary>
    [DataField]
    public ProtoId<AlertPrototype> Alert = "WolfmedDowned";

    /// <summary>The hands have already let go for this spell on the floor. One drop per Downed, not per tick.</summary>
    [ViewVariables]
    public bool Dropped;
}
