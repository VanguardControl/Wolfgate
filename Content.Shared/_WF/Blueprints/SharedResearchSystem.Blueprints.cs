using Content.Shared._WF.Blueprints;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Research.Systems;

public abstract partial class SharedResearchSystem
{
    private Dictionary<string, int>? _wfHuntTiers;
    private bool _wfReloadSubscribed;

    /// <summary>Hunt tier of each technology a blueprint pattern recovers.</summary>
    private Dictionary<string, int> WFHuntTiers()
    {
        if (_wfHuntTiers != null)
            return _wfHuntTiers;

        _wfHuntTiers = new Dictionary<string, int>();
        foreach (var pattern in PrototypeManager.EnumeratePrototypes<WFBlueprintPatternPrototype>())
        {
            if (pattern.Technology is { } tech)
                _wfHuntTiers[tech] = pattern.Tier;
        }

        if (!_wfReloadSubscribed)
        {
            PrototypeManager.PrototypesReloaded += WFOnPrototypesReloaded;
            _wfReloadSubscribed = true;
        }

        return _wfHuntTiers;
    }

    /// <summary>Whether a blueprint pattern names the technology, so only a hunt can unlock it.</summary>
    public bool WFIsHuntOnly(string technology)
    {
        return WFHuntTiers().ContainsKey(technology);
    }

    /// <summary>How the technology is obtained, shown where upstream showed its point cost.</summary>
    public void WFAddRecoveryLine(FormattedMessage description, TechnologyPrototype technology)
    {
        description.AddMarkupOrThrow(WFHuntTiers().TryGetValue(technology.ID, out var tier)
            ? Loc.GetString("wf-codex-recovered-by", ("tier", tier))
            : Loc.GetString("wf-codex-tier-inherited"));
    }

    private void WFOnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WFBlueprintPatternPrototype>())
            _wfHuntTiers = null;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_wfReloadSubscribed)
            PrototypeManager.PrototypesReloaded -= WFOnPrototypesReloaded;
        _wfReloadSubscribed = false;
    }
}
