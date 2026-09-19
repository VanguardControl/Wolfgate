using Content.Shared.Construction.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>The hull machine that cuts a chunk free; its part tier sets how long the crack takes.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFGravityProjectorComponent : Component
{
    /// <summary>Crack-time multiplier from machine parts: 1.0 at tier 1 down to 0.7 at tier 4 (design D12).</summary>
    [DataField, AutoNetworkedField]
    public float CrackTimeMultiplier = 1f;

    /// <summary>Per-tier scaling factor; 0.888^3 is 0.70, so tier 4 hits the design's floor.</summary>
    [DataField]
    public float PartScaling = 0.888f;

    /// <summary>Which part type drives the multiplier.</summary>
    [DataField]
    public ProtoId<MachinePartPrototype> RatedPart = "Capacitor";

    /// <summary>Set by the Destructible Breakage threshold; there is no engine-side broken flag.</summary>
    [DataField, AutoNetworkedField]
    public bool Broken;

    /// <summary>What the projector is doing, for the sprite and for F4's grace timer.</summary>
    [DataField, AutoNetworkedField]
    public WFProjectorState State = WFProjectorState.Off;

    /// <summary>
    /// The grid-local facing the mapper gave the mount, taken the first sweep a cut swings it onto an anchor and put
    /// back the sweep the cut ends. Null means the mount is wherever it was placed and nothing has moved it.
    /// LOCAL rather than world on purpose: the hull is snapped onto the cut circle mid-crack and can be flown away
    /// afterwards, so only the local pose still means "outward, as placed" when the facing is handed back.
    /// Deliberately not networked - TransformComponent's own rotation is what the client draws.
    /// </summary>
    [DataField]
    public Angle? PlacedRotation;
}
