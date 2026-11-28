using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Blueprints;

/// <summary>
/// The Artifact Blueprint Locator, on the R&amp;D console: lists patterns, starts hunts, decodes shards into Constructs and
/// takes Constructs.
/// </summary>
[RegisterComponent]
public sealed partial class WGBlueprintLocatorComponent : Component
{
    /// <summary>The locate fee by tier, taken from the requester's bank account.</summary>
    [DataField]
    public int[] Fees = { 5000, 15000, 40000 };

    [DataField]
    public int MaxPatterns = 4;

    /// <summary>How long after a hunt ends before the next can start.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    /// <summary>Seconds to decode a shard, by tier.</summary>
    [DataField]
    public float[] DecodeTimes = { 10f, 25f, 45f };

    [DataField]
    public EntProtoId Construct = "WGBlueprintConstruct";

    [ViewVariables]
    public WGBlueprintHunt? Hunt;

    [ViewVariables]
    public TimeSpan NextHunt;
}

/// <summary>A running hunt: the claimed site and the shards still out.</summary>
public sealed class WGBlueprintHunt
{
    public EntityUid Ground;
    public int SiteIndex;
    public int Tier;
    public List<ProtoId<WGBlueprintPatternPrototype>> Patterns = new();
    public HashSet<EntityUid> Shards = new();
}

/// <summary>A pattern shard, recovered from a site; a research console decodes it into a Construct.</summary>
[RegisterComponent]
public sealed partial class WGBlueprintShardComponent : Component
{
    [DataField]
    public ProtoId<WGBlueprintPatternPrototype>? Pattern;

    /// <summary>The locator whose hunt placed it; null once that hunt ends.</summary>
    [ViewVariables]
    public EntityUid? Locator;
}

/// <summary>A decoded pattern; using it on a research console unlocks the pattern on that console's server.</summary>
[RegisterComponent]
public sealed partial class WGBlueprintConstructComponent : Component
{
    [DataField]
    public ProtoId<WGBlueprintPatternPrototype>? Pattern;
}
