using Content.Shared.Tools;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Anchors;

/// <summary>
/// A shipping crate that pries open on a planet ground layer to leave one gravity anchor standing in its place.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFAnchorCrateComponent : Component
{
    /// <summary>What unpacking spawns.</summary>
    [DataField]
    public EntProtoId Contents = "WFGravityAnchor";

    /// <summary>Tool quality needed to crack the crate open.</summary>
    [DataField]
    public ProtoId<ToolQualityPrototype> Tool = "Prying";

    /// <summary>Seconds of do-after to unpack.</summary>
    [DataField]
    public float Delay = 6f;

    /// <summary>The cracker grid this crate (and the anchor inside it) belongs to.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Cracker;

    /// <summary>Half-width of the square the unpacked anchor needs free, in tiles.</summary>
    [DataField]
    public int FootprintRadius = 1;

    /// <summary>Mass this adds to a carrying hull's gravgen load (design D11).</summary>
    [DataField]
    public float VirtualMass = 6f;
}
