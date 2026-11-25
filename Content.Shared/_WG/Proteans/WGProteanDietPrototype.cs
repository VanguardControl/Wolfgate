using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Proteans;

/// <summary>
/// What a Protean gets from each material it absorbs, read against an item's <c>PhysicalCompositionComponent</c>.
/// An item's yield is the sum over its listed materials of units times <see cref="WGProteanDietEntry.Mass"/>; an
/// item made only of unlisted materials offers no Absorb verb. Named by <see cref="WGNanomassComponent.Diet"/>.
/// </summary>
[Prototype("wgProteanDiet")]
public sealed partial class WGProteanDietPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Per material: mass per material unit, and the trace it leaves.</summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<MaterialPrototype>, WGProteanDietEntry> Materials = new();

    /// <summary>How long a trace lasts; absorbing the material again resets it to this.</summary>
    [DataField]
    public TimeSpan TraceDuration = TimeSpan.FromMinutes(10);
}

/// <summary>One material in a Protean's diet.</summary>
[DataDefinition]
public sealed partial class WGProteanDietEntry
{
    /// <summary>Mass per material unit. A sheet is 100 units, so steel's 0.1 gives 10 mass a sheet.</summary>
    [DataField(required: true)]
    public float Mass;

    /// <summary>The status effect absorbing this material leaves, if any; it carries a <see cref="WGProteanTraceComponent"/>.</summary>
    [DataField]
    public EntProtoId? Trace;
}
