using Content.Shared.Database;

namespace Content.Client._WG.LogInvestigator;

/// <summary>Colours shared by the admin log views.</summary>
public static class LogInvestigatorStyle
{
    /// <summary>The colour a log's type tag and impact bar take.</summary>
    public static Color ImpactColor(LogImpact impact)
    {
        return impact switch
        {
            LogImpact.Extreme => Color.FromHex("#ff5c5c"),
            LogImpact.High => Color.FromHex("#ff9f43"),
            LogImpact.Medium => Color.FromHex("#ffd166"),
            _ => Color.FromHex("#8fa3bf"),
        };
    }
}
