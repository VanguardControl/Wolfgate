using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Proteans;

/// <summary>A Protean's body: no surgery, no hardsuits.</summary>
[RegisterComponent]
public sealed partial class WGProteanComponent : Component
{
    /// <summary>Clothing with this tag, and anything attached to such clothing, can't be put on the Protean.</summary>
    [DataField]
    public ProtoId<TagPrototype> HardsuitTag = "Hardsuit";

    /// <summary>Why a hardsuit piece won't go on.</summary>
    [DataField]
    public LocId HardsuitRefuse = "wg-protean-hardsuit-refuse";

    /// <summary>Damage from which examining shows the light, medium and heavy hurt lines.</summary>
    [DataField]
    public float HurtLight = 10f;

    [DataField]
    public float HurtMedium = 30f;

    [DataField]
    public float HurtHeavy = 55f;

    /// <summary>Popup when someone tries to operate.</summary>
    [DataField]
    public LocId SurgeryRefuse = "wg-protean-surgery-refuse";
}
