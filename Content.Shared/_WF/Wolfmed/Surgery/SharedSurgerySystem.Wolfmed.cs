// WOLFGATE: HOOK 24 and HOOK 25 bodies. Kept out of the upstream SharedSurgerySystem.cs so that file carries
// only the two marked call sites and gains neither a using nor a dependency line.

using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared.Body.Part;

namespace Content.Shared._Shitmed.Medical.Surgery;

public abstract partial class SharedSurgerySystem
{
    [Dependency] private WoundSystem _wolfmedWounds = default!; // WOLFGATE: HOOK 25
    [Dependency] private WolfmedSurgeryConditionSystem _wolfmedConditions = default!; // WOLFGATE: HOOK 24

    /// <summary>HOOK 24 body: true when a wound-severity window is declared and the part is outside it.</summary>
    private bool WolfmedWoundWindowFails(Entity<SurgeryWoundedConditionComponent> ent, EntityUid body, EntityUid part)
    {
        // Both bounds null is the shipped pre-Wolfmed shape, and no non-wound-host part can carry a wound at
        // all, so the two existing tend surgeries and every non-host are provably unchanged (D2).
        if (ent.Comp.MinWoundSeverity is null && ent.Comp.MaxWoundSeverity is null ||
            !HasComp<WoundHostComponent>(body))
            return false;

        var severity = _wolfmedConditions.GetGroupSeverity(part, ent.Comp.WoundGroup);
        return ent.Comp.MinWoundSeverity is { } min && severity < min ||
               ent.Comp.MaxWoundSeverity is { } max && severity > max;
    }

    /// <summary>HOOK 25 body: true while a stump still carries an untreated AmputationConsequenceWound.</summary>
    private bool WolfmedStumpBlocksAttachment(EntityUid parent)
    {
        return TryComp(parent, out BodyPartComponent? bodyPart) && bodyPart.Body is { } body &&
               TryComp(body, out WoundHostComponent? host) &&
               _wolfmedWounds.GetWounds(parent).Any(wound => wound.Comp.Prototype == host.AmputationConsequenceWound);
    }
}
