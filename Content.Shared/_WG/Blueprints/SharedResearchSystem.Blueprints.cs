using Content.Shared._WG.Blueprints;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Research.Systems;

public abstract partial class SharedResearchSystem
{
    private Dictionary<string, int>? _wgHuntTiers;
    private bool _wgReloadSubscribed;

    /// <summary>Hunt tier of each technology a blueprint pattern recovers.</summary>
    private Dictionary<string, int> WGHuntTiers()
    {
        if (_wgHuntTiers != null)
            return _wgHuntTiers;

        _wgHuntTiers = new Dictionary<string, int>();
        foreach (var pattern in PrototypeManager.EnumeratePrototypes<WGBlueprintPatternPrototype>())
        {
            if (pattern.Technology is { } tech)
                _wgHuntTiers[tech] = pattern.Tier;
        }

        if (!_wgReloadSubscribed)
        {
            PrototypeManager.PrototypesReloaded += WGOnPrototypesReloaded;
            _wgReloadSubscribed = true;
        }

        return _wgHuntTiers;
    }

    /// <summary>Whether a blueprint pattern names the technology, so only a hunt can unlock it.</summary>
    public bool WGIsHuntOnly(string technology)
    {
        return WGHuntTiers().ContainsKey(technology);
    }

    /// <summary>How the technology is obtained, shown where upstream showed its point cost.</summary>
    public void WGAddRecoveryLine(FormattedMessage description, TechnologyPrototype technology)
    {
        description.AddMarkupOrThrow(WGHuntTiers().TryGetValue(technology.ID, out var tier)
            ? Loc.GetString("wg-codex-recovered-by", ("tier", tier))
            : Loc.GetString("wg-codex-tier-inherited"));
    }

    private void WGOnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WGBlueprintPatternPrototype>())
            _wgHuntTiers = null;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_wgReloadSubscribed)
            PrototypeManager.PrototypesReloaded -= WGOnPrototypesReloaded;
        _wgReloadSubscribed = false;
    }
}
