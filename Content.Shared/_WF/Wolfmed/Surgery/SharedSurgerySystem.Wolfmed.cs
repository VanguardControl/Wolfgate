// WOLFGATE: HOOK 24, 25, 26 and 27 bodies. Kept out of the upstream SharedSurgerySystem files so those
// carry only the marked call sites and gain neither a using nor a dependency line.

using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;

namespace Content.Shared._Shitmed.Medical.Surgery;

public abstract partial class SharedSurgerySystem
{
    [Dependency] private WoundSystem _wolfmedWounds = default!; // WOLFGATE: HOOK 25
    [Dependency] private WolfmedSurgeryConditionSystem _wolfmedConditions = default!; // WOLFGATE: HOOK 24
    [Dependency] private IConfigurationManager _wolfmedCfg = default!; // WOLFGATE: HOOK 27
    [Dependency] private WolfmedWoundDamageSyncSystem _wolfmedDamageSync = default!; // WOLFGATE (AUTODOC5): HOOK 27

    /// <summary>
    /// HOOK 24 body: whether a tend surgery should stay off this part. Upstream lists it wherever the BODY
    /// carries any damage at all, so one cut had the pod's planner queueing a tend on every limb; on a wound
    /// host the part has to carry something tending could actually close, and then be inside whatever
    /// severity window the surgery declares (P4-D19). Non-hosts are provably unchanged (D2).
    /// </summary>
    private bool WolfmedWoundWindowFails(Entity<SurgeryWoundedConditionComponent> ent, EntityUid body, EntityUid part)
    {
        if (!HasComp<WoundHostComponent>(body))
            return false;

        var severity = _wolfmedConditions.GetTreatableGroupSeverity(part, ent.Comp.WoundGroup);
        if (severity <= FixedPoint2.Zero)
            return true;

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

    /// <summary>
    /// HOOK 26 body: whether a tend step still has work to do, for a wound host. Upstream reads the whole
    /// BODY's damage of the group, so tending a torso could never finish while a hand still had a scratch
    /// and the step repeated for ever. On a wound host the answer is the part the surgeon is holding open,
    /// and what is wrong with a Wolfmed part is its wounds: the step runs until they are closed and stops,
    /// whatever the routing's damage bookkeeping still says. Null for everything else, which leaves the
    /// shipped behaviour exactly as it was (D2).
    /// </summary>
    private bool? WolfmedTendPending(EntityUid body, EntityUid part, string mainGroup, string[] types)
    {
        if (!HasComp<WoundHostComponent>(body))
            return null;

        return _wolfmedConditions.GetTreatableGroupSeverity(part, mainGroup) > FixedPoint2.Zero;
    }

    /// <summary>
    /// HOOK 27 body: a tend pass closes the part's own wounds at the strength a suture does. The damage the
    /// step removes reaches a wound through the routing's HealingMultiplier, a tenth of what came off, which
    /// left a surgeon picking at one cut for twenty passes; tending is meant to be the surgical way to close
    /// cuts and bruises (D7, W7). Wounds nothing closes and wounds refusing treatment are untouched, because
    /// TreatWound raises the same attempt event a dressing does.
    /// </summary>
    private void WolfmedTendWounds(EntityUid body, EntityUid part, string mainGroup)
    {
        if (!HasComp<WoundHostComponent>(body))
            return;

        var strength = FixedPoint2.New(MathF.Max(0f, _wolfmedCfg.GetCVar(WolfmedCVars.SurgeryTendStrength)));
        if (strength <= FixedPoint2.Zero)
            return;

        var healing = new DamageSpecifier();
        foreach (var type in mainGroup == "Brute" ? BruteDamageTypes : BurnDamageTypes)
            healing.DamageDict[type] = -strength;

        _wolfmedWounds.TryHealWounds(part, healing);

        // AUTODOC5: closing the wound by hand leaves the damage it was made from on the part, and the body
        // totals every part, so a patient tended back to no wounds still read as hurt and only a brute pack
        // could clear it. The part now gives up the damage its wounds no longer account for.
        _wolfmedDamageSync.SyncPart(part);
    }
}
