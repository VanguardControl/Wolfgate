using Content.Shared.Database;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.LogInvestigator;

/// <summary>A group of admin log types the investigator filters by. Every type belongs to one.</summary>
[Prototype("wgLogCategory")]
public sealed partial class WGLogCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>Lower comes first.</summary>
    [DataField]
    public int Order;

    [DataField(required: true)]
    public List<LogType> Types = new();
}
