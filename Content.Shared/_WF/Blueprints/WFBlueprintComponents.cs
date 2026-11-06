using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Blueprints;

/// <summary>The Artifact Blueprint Locator: lists patterns, starts hunts and takes Constructs.</summary>
[RegisterComponent]
public sealed partial class WFBlueprintLocatorComponent : Component
{
    /// <summary>The locate fee by tier, taken from the requester's bank account.</summary>
    [DataField]
    public int[] Fees = { 5000, 15000, 40000 };

    [DataField]
    public int MaxPatterns = 4;

    /// <summary>How long after a hunt ends before the next can start.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    [ViewVariables]
    public WFBlueprintHunt? Hunt;

    [ViewVariables]
    public TimeSpan NextHunt;
}

/// <summary>A running hunt: the claimed site and the shards still out.</summary>
public sealed class WFBlueprintHunt
{
    public EntityUid Ground;
    public int SiteIndex;
    public int Tier;
    public List<ProtoId<WFBlueprintPatternPrototype>> Patterns = new();
    public HashSet<EntityUid> Shards = new();
}

/// <summary>A pattern shard, recovered from a site; the Blueprint Assembler turns it into a Construct.</summary>
[RegisterComponent]
public sealed partial class WFBlueprintShardComponent : Component
{
    [DataField]
    public ProtoId<WFBlueprintPatternPrototype>? Pattern;

    /// <summary>The locator whose hunt placed it; null once that hunt ends.</summary>
    [ViewVariables]
    public EntityUid? Locator;
}

/// <summary>An assembled pattern; inserting it into a locator unlocks the pattern on the locator's server.</summary>
[RegisterComponent]
public sealed partial class WFBlueprintConstructComponent : Component
{
    [DataField]
    public ProtoId<WFBlueprintPatternPrototype>? Pattern;
}

/// <summary>The Blueprint Assembler: turns a shard into a Construct.</summary>
[RegisterComponent]
public sealed partial class WFBlueprintAssemblerComponent : Component
{
    /// <summary>Seconds to assemble a shard, by tier.</summary>
    [DataField]
    public float[] Times = { 10f, 25f, 45f };

    [DataField]
    public EntProtoId Construct = "WFBlueprintConstruct";
}
