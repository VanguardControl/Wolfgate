using System.Linq;
using Content.Shared._WF.Biotype;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>The Biotype dropdown in the Body card: organic or synthetic, whatever the species.</summary>
public sealed partial class HumanoidProfileEditor
{
    private void InitializeBiotype()
    {
        foreach (var biotype in _prototypeManager.EnumeratePrototypes<WFBiotypePrototype>().OrderBy(p => p.Biotype))
        {
            BiotypeButton.AddItem(Loc.GetString(biotype.Name), (int) biotype.Biotype);
        }

        BiotypeButton.OnItemSelected += args =>
        {
            BiotypeButton.SelectId(args.Id);
            SetBiotype((WFBiotype) args.Id);
        };
    }

    /// <summary>Shows the biotype the character will have, the species' own while none is picked.</summary>
    private void UpdateBiotype()
    {
        if (Profile == null || !_prototypeManager.TryIndex<SpeciesPrototype>(Profile.Species, out var species))
            return;

        BiotypeButton.SelectId((int) WFBiotypeRules.Resolve(Profile.Biotype, species));
    }

    private void SetBiotype(WFBiotype biotype)
    {
        if (Profile == null || Profile.Biotype == biotype)
            return;

        Profile = Profile.WithBiotype(biotype);
        SetDirty();

        // Traits and loadouts can list biotypes they refuse.
        RefreshTraits();
        RefreshLoadouts();
    }

    /// <summary>The tooltip line naming the biotypes that can't take the trait, or null when it lists none.</summary>
    private string? BiotypeBlacklistTooltip(TraitPrototype trait)
    {
        if (trait.BiotypeBlacklist.Count == 0)
            return null;

        var names = trait.BiotypeBlacklist
            .OrderBy(b => b)
            .Select(b => $"[color=#087209]{Loc.GetString(WFBiotypeGates.NameKey(b))}[/color]");
        return Loc.GetString("wf-biotype-trait-blocked", ("biotypes", string.Join(", ", names)));
    }
}
