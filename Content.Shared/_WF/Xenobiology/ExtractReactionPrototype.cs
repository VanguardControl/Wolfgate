using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenobiology;

/// <summary>
/// Reagents a slime extract needs and the effects it fires once it has them.
/// </summary>
[Prototype]
public sealed partial class ExtractReactionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Minimum amount of each reagent.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Requirements = new();

    [DataField(required: true, serverOnly: true)]
    public List<ScaledEntityEffect> Effects = new();

    /// <summary>
    /// Whether the extract is deleted once it runs out of uses.
    /// </summary>
    [DataField(required: true)]
    public bool ShouldDelete;

    /// <summary>
    /// Time between the requirements being met and the effects firing.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.Zero;
}

/// <summary>
/// An effect whose strength scales with the reagent supplied.
/// </summary>
/// <remarks>
/// The ratio is the smallest of supplied / required over all requirements; the strength is
/// <c>ratio * ScalingFactor + ScalingOffset</c>.
/// </remarks>
[DataDefinition]
public sealed partial class ScaledEntityEffect
{
    [DataField(required: true)]
    public EntityEffect Effect = default!;

    [DataField]
    public FixedPoint2 ScalingFactor = 0;

    [DataField]
    public FixedPoint2 ScalingOffset = 1;
}
