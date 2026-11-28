using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Proteans;

/// <summary>
/// Marks a Protean's body for <see cref="WGProteanSystem"/>: it can't be operated on, won't wear hardsuits and
/// describes its damage on examine. Nanomass lives on <see cref="WGNanomassComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class WGProteanComponent : Component
{
    /// <summary>Clothing with this tag, and anything attached to such clothing, can't be put on the Protean.</summary>
    [DataField]
    public ProtoId<TagPrototype> HardsuitTag = "Hardsuit";

    /// <summary>Why a hardsuit piece won't go on; shown to whoever tried, the Protean or someone dressing it.</summary>
    [DataField]
    public LocId HardsuitRefuse = "wg-protean-hardsuit-refuse";

    /// <summary>Total damage from which examining shows <c>wg-protean-examine-hurt-light</c>.</summary>
    [DataField]
    public float HurtLight = 10f;

    /// <summary>Total damage from which examining shows <c>wg-protean-examine-hurt-medium</c>.</summary>
    [DataField]
    public float HurtMedium = 30f;

    /// <summary>Total damage from which examining shows <c>wg-protean-examine-hurt-heavy</c>.</summary>
    [DataField]
    public float HurtHeavy = 55f;

    /// <summary>Popup for whoever uses a surgical tool's verb on the Protean.</summary>
    [DataField]
    public LocId SurgeryRefuse = "wg-protean-surgery-refuse";
}
