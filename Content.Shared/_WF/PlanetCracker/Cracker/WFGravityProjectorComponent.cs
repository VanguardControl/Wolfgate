using Content.Shared.Construction.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>The hull machine that cuts a chunk free; its part tier sets how long the crack takes.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFGravityProjectorComponent : Component
{
    /// <summary>Crack-time multiplier from machine parts: 1.0 at tier 1 down to 0.7 at tier 4.</summary>
    [DataField, AutoNetworkedField]
    public float CrackTimeMultiplier = 1f;

    /// <summary>Per-tier scaling factor; 0.888^3 is 0.70, so tier 4 hits the floor.</summary>
    [DataField]
    public float PartScaling = 0.888f;

    /// <summary>Which part type drives the multiplier.</summary>
    [DataField]
    public ProtoId<MachinePartPrototype> RatedPart = "Capacitor";

    /// <summary>Set by the Destructible Breakage threshold; there is no engine-side broken flag.</summary>
    [DataField, AutoNetworkedField]
    public bool Broken;

    /// <summary>What the projector is doing, for the sprite and the grace timer.</summary>
    [DataField, AutoNetworkedField]
    public WFProjectorState State = WFProjectorState.Off;

    /// <summary>Grid-local facing the mapper gave the mount, saved while a cut swings it and restored after; null when unmoved.</summary>
    [DataField]
    public Angle? PlacedRotation;
}
