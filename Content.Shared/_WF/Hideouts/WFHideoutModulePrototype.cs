using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Hideouts;

/// <summary>What the Hideout tab lists a module under.</summary>
[Serializable, NetSerializable]
public enum WFHideoutModuleCategory : byte
{
    Ships,
    Living,
    Work,
}

/// <summary>Which hangar a module's prefab is; an outpost holds one of each.</summary>
[Serializable, NetSerializable]
public enum WFHideoutBay : byte
{
    None,
    Faction,
    Shipwright,
}

/// <summary>A building an outpost's owner buys outright: a survey stake that lays its prefab, or a kit of packed machines.</summary>
[Prototype("wfHideoutModule")]
public sealed partial class WFHideoutModulePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>Where it sits in its category, lowest first.</summary>
    [DataField]
    public int Order;

    [DataField]
    public WFHideoutModuleCategory Category = WFHideoutModuleCategory.Living;

    /// <summary>Whether only a company's outpost buys it.</summary>
    [DataField]
    public bool Company;

    [DataField(required: true)]
    public int Price;

    /// <summary>The hangar its prefab is; one that already stands on the outpost isn't sold again.</summary>
    [DataField]
    public WFHideoutBay Bay;

    /// <summary>A grid the owner lays onto the outpost with the survey stake a purchase hands out; under SharedMaps.</summary>
    [DataField]
    public ResPath? Prefab;

    /// <summary>The layouts the stake can be set to, the prefab among them; empty when there is only the prefab.</summary>
    [DataField]
    public List<WFHideoutLayout> Layouts = new();

    /// <summary>Things left at the console with a purchase.</summary>
    [DataField]
    public List<EntProtoId> Grants = new();
}

/// <summary>One of the layouts a module's stake can lay.</summary>
[DataDefinition]
public sealed partial class WFHideoutLayout
{
    [DataField(required: true)]
    public LocId Name;

    /// <summary>The layout's grid; under SharedMaps.</summary>
    [DataField(required: true)]
    public ResPath Prefab;
}
