using Content.Shared._WF.Blueprints;
using Robust.Shared.Prototypes;

namespace Content.Shared.Research.Systems;

public abstract partial class SharedResearchSystem
{
    private HashSet<string>? _wfHuntOnly;

    /// <summary>Whether a blueprint pattern names the technology, so only a hunt can unlock it.</summary>
    public bool WFIsHuntOnly(string technology)
    {
        if (_wfHuntOnly == null)
        {
            _wfHuntOnly = new HashSet<string>();
            foreach (var pattern in PrototypeManager.EnumeratePrototypes<WFBlueprintPatternPrototype>())
            {
                if (pattern.Technology is { } tech)
                    _wfHuntOnly.Add(tech);
            }

            PrototypeManager.PrototypesReloaded += WFOnPrototypesReloaded;
        }

        return _wfHuntOnly.Contains(technology);
    }

    private void WFOnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WFBlueprintPatternPrototype>())
            _wfHuntOnly = null;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_wfHuntOnly != null)
            PrototypeManager.PrototypesReloaded -= WFOnPrototypesReloaded;
    }
}
