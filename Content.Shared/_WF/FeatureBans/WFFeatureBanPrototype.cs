using Robust.Shared.Prototypes;

namespace Content.Shared._WF.FeatureBans;

/// <summary>
/// A feature players can be banned from, like a job. The ban is a role ban on <c>Feature:&lt;id&gt;</c>, so it is
/// made, listed and pardoned with the usual ban tools.
/// </summary>
[Prototype("wfFeatureBan")]
public sealed partial class WFFeatureBanPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Shown in the ban panel and to the banned player.</summary>
    [DataField(required: true)]
    public LocId Name;
}

/// <summary>Shared names for feature bans.</summary>
public static class WFFeatureBans
{
    /// <summary>What a feature ban's role string starts with, beside upstream's <c>Job:</c>.</summary>
    public const string Prefix = "Feature:";

    public static string Role(string feature) => Prefix + feature;
}
