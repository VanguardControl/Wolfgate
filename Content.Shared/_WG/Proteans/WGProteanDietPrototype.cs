using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Proteans;

/// <summary>What a Protean gets from each material it absorbs.</summary>
[Prototype("wgProteanDiet")]
public sealed partial class WGProteanDietPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Per material: mass per material unit, and the trace it leaves.</summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<MaterialPrototype>, WGProteanDietEntry> Materials = new();

    /// <summary>How long a trace lasts.</summary>
    [DataField]
    public TimeSpan TraceDuration = TimeSpan.FromMinutes(10);
}

/// <summary>One material in a Protean's diet.</summary>
[DataDefinition]
public sealed partial class WGProteanDietEntry
{
    /// <summary>Mass per material unit; a sheet is 100 units.</summary>
    [DataField(required: true)]
    public float Mass;

    /// <summary>The status effect absorbing this material leaves, if any.</summary>
    [DataField]
    public EntProtoId? Trace;
}
