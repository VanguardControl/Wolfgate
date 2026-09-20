using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Armor;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Wolfmed.Examine;

/// <summary>
/// LOOK: the health examine on a wound host, rewritten as what a person could actually see. Every line it
/// prints is something visible on the body in front of the examiner; numbers, rates, severities and
/// anything under the skin stay on the health analyzer.
/// </summary>
/// <remarks>
/// Built on demand from the examine verb, which runs on the server, so it reads server-side wound state
/// directly and networks nothing new. What each wound looks like is data
/// (<see cref="WolfmedWoundLookPrototype"/>), not a switch: no wound id appears in this file.
/// </remarks>
public sealed class WolfmedVisualInspectionSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;

    /// <summary>Wound prototype id to its appearance. Rebuilt whenever prototypes are reloaded.</summary>
    private readonly Dictionary<string, WolfmedWoundLookPrototype> _looks = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        BuildIndex();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WolfmedWoundLookPrototype>())
            BuildIndex();
    }

    private void BuildIndex()
    {
        _looks.Clear();
        foreach (var look in _prototypes.EnumeratePrototypes<WolfmedWoundLookPrototype>())
            _looks[look.Wound.Id] = look;
    }

    /// <summary>The appearance declared for a wound prototype, if one is.</summary>
    public bool TryGetLook(string wound, [NotNullWhen(true)] out WolfmedWoundLookPrototype? look) =>
        _looks.TryGetValue(wound, out look);

    /// <summary>
    /// Appends the visual inspection of a wound host to an examine message. <paramref name="detailed"/> is
    /// the examiner's details range; examining yourself is always detailed and never hidden by clothing.
    /// </summary>
    public void AddLookMarkup(EntityUid examined, EntityUid examiner, FormattedMessage message, bool detailed)
    {
        var self = examined == examiner;
        detailed |= self;

        if (!_prototypes.TryIndex<WolfmedLookProfilePrototype>(WolfmedLookProfilePrototype.Default, out var profile))
            return;

        var identity = Identity.Entity(examined, EntityManager);
        if (!message.IsEmpty)
            message.PushNewline();

        message.AddMarkupOrThrow(Loc.GetString(self ? "wolfmed-look-title-self" : "wolfmed-look-title-other",
            ("target", identity)));

        var parts = _body.GetBodyChildren(examined)
            .OrderBy(part => PartOrder(part.Component.PartType))
            .ThenBy(part => (int) part.Component.Symmetry)
            .ToList();

        var coverage = self ? null : GetCoverage(examined, profile);
        var findings = new List<string>();
        var hidden = false;
        var lines = 0;

        foreach (var (part, component) in parts)
        {
            findings.Clear();
            var covered = coverage != null && IsCovered(coverage, component.PartType, component.Symmetry);
            hidden |= AddPartFindings(part, self, detailed, covered, profile, findings);
            if (findings.Count == 0)
                continue;

            message.PushNewline();
            message.AddMarkupOrThrow(Loc.GetString(self ? "wolfmed-look-part-self" : "wolfmed-look-part-other",
                ("target", identity),
                ("part", PartName(part, component)),
                ("findings", string.Join(", ", findings))));
            lines++;
        }

        // Sepsis is the one finding that belongs to the whole patient rather than to a part.
        if (detailed && TryComp(examined, out WolfmedSepsisComponent? sepsis) &&
            sepsis.Progress >= profile.SepsisVisibleAt)
        {
            message.PushNewline();
            message.AddMarkupOrThrow(Loc.GetString(self ? "wolfmed-look-sepsis-self" : "wolfmed-look-sepsis-other",
                ("target", identity)));
            lines++;
        }

        if (lines == 0)
        {
            // Nothing shown at all reads differently when there was something and the clothing took it.
            message.PushNewline();
            message.AddMarkupOrThrow(Loc.GetString(
                hidden ? "wolfmed-look-hidden" : self ? "wolfmed-look-none-self" : "wolfmed-look-none-other",
                ("target", identity)));
        }
        else if (hidden)
        {
            message.PushNewline();
            message.AddMarkupOrThrow(Loc.GetString("wolfmed-look-covered"));
        }

        if (!detailed)
        {
            message.PushNewline();
            message.AddMarkupOrThrow(Loc.GetString("wolfmed-look-distant"));
        }
    }

    /// <summary>Collects everything visible on one part. Returns true when something was hidden by clothing.</summary>
    private bool AddPartFindings(
        EntityUid part,
        bool self,
        bool detailed,
        bool covered,
        WolfmedLookProfilePrototype profile,
        List<string> findings)
    {
        var mechanical = _traits.IsMechanical(part);
        var suffix = mechanical ? "-mechanical" : string.Empty;
        var seen = new HashSet<string>();
        var hidden = false;
        var rate = 0f;
        var treatment = BleedingTreatment.None;
        var infection = WolfmedInfectionStage.None;
        var scars = 0;

        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State == WoundState.Healed)
                continue;

            if (HasComp<WoundScarComponent>(wound))
            {
                scars++;
                continue;
            }

            if (TryComp(wound, out WoundBleedingComponent? bleeding))
            {
                // A dressed wound does not read as actively bleeding; the dressing is the finding instead.
                if (bleeding.Treatment != BleedingTreatment.None)
                    treatment = (BleedingTreatment) Math.Max((byte) treatment, (byte) bleeding.Treatment);
                else
                    rate += bleeding.CurrentRate;
            }

            if (TryComp(wound, out WolfmedInfectionComponent? infected) && infected.Stage > infection)
                infection = infected.Stage;

            if (!_looks.TryGetValue(wound.Comp.Prototype.Id, out var look) ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype))
                continue;

            var finding = look.Resolve(prototype.GetStage(wound.Comp.Severity));
            if (finding.Description is not { } description || finding.Visibility == WolfmedLookVisibility.None)
            {
                // Nothing to see. The patient still knows their own body hurts in a particular way.
                if (self && finding.SelfHint is { } hint)
                    Add(findings, seen, hint);
                continue;
            }

            if (!detailed && !finding.Distant)
                continue;

            if (covered && finding.Visibility != WolfmedLookVisibility.Clothed)
            {
                hidden = true;
                continue;
            }

            Add(findings, seen, description);
        }

        if (rate > profile.BleedOozing && (detailed || rate >= profile.DistantBleed))
        {
            var band = rate >= profile.BleedSpurting ? "spurting"
                : rate >= profile.BleedFlowing ? "flowing"
                : "oozing";

            if (!covered)
                Add(findings, seen, "wolfmed-look-bleed-" + band + suffix);
            else if (rate >= profile.SoakThrough)
                Add(findings, seen, "wolfmed-look-soaking" + suffix);
            else
                hidden = true;
        }

        // Dressings sit against the skin; a splint or a tourniquet is strapped over whatever is worn.
        if (treatment != BleedingTreatment.None && detailed)
        {
            if (covered)
                hidden = true;
            else
                Add(findings, seen, "wolfmed-look-treatment-" + treatment.ToString().ToLowerInvariant());
        }

        if (detailed && TryComp(part, out WolfmedSplintMarkComponent? splint))
            Add(findings, seen, "wolfmed-look-splint-" + splint.Overlay.ToString().ToLowerInvariant());

        if (detailed && HasComp<WolfmedTourniquetComponent>(part))
            Add(findings, seen, "wolfmed-look-tourniquet");

        if (detailed && infection is WolfmedInfectionStage.Local or WolfmedInfectionStage.Spreading)
        {
            if (covered)
                hidden = true;
            else
                Add(findings, seen, "wolfmed-look-infection-" + infection.ToString().ToLowerInvariant());
        }

        if (detailed && scars > 0)
        {
            if (covered)
                hidden = true;
            else
                findings.Add(Loc.GetString("wolfmed-look-scars", ("count", scars)));
        }

        if (self && detailed && TryComp(part, out PainComponent? pain))
        {
            if (pain.Suppression > 0)
                Add(findings, seen, "wolfmed-look-numb");

            if (GetPainLevel(part) is { } level)
                Add(findings, seen, "health-examinable-pain-" + level);
        }

        return hidden;
    }

    /// <summary>Adds a localized finding once per key, so two identical wounds read as one observation.</summary>
    private void Add(List<string> findings, HashSet<string> seen, string key)
    {
        if (seen.Add(key))
            findings.Add(Loc.GetString(key));
    }

    /// <summary>Onyx's own pain bands, kept so the self line reads exactly as it did before.</summary>
    private string? GetPainLevel(EntityUid part)
    {
        var value = _pain.GetPain(part);
        return value >= 50 ? "agony"
            : value >= 30 ? "terrible"
            : value >= 15 ? "strong"
            : value > 0 ? "light"
            : null;
    }

    /// <summary>What every worn item hides, resolved once per examine.</summary>
    private List<(HashSet<BodyPartType> Parts, HashSet<BodyPartSymmetry>? Sides)> GetCoverage(
        EntityUid examined,
        WolfmedLookProfilePrototype profile)
    {
        var coverage = new List<(HashSet<BodyPartType> Parts, HashSet<BodyPartSymmetry>? Sides)>();
        if (!_inventory.TryGetContainerSlotEnumerator(examined, out var slots, SlotFlags.WITHOUT_POCKET))
            return coverage;

        while (slots.NextItem(out var item, out var slot))
        {
            // Locational armour already says what it protects; ordinary clothing falls back to its slot.
            if (TryComp(item, out ArmorComponent? armor) && armor.Coverage is { Count: > 0 } parts)
            {
                coverage.Add((parts, armor.CoverageSymmetry));
                continue;
            }

            if (GetSlotCoverage(profile, slot.SlotFlags) is { } fallback)
                coverage.Add((fallback, null));
        }

        return coverage;
    }

    private static HashSet<BodyPartType>? GetSlotCoverage(WolfmedLookProfilePrototype profile, SlotFlags flags)
    {
        foreach (var entry in profile.Coverage)
        {
            foreach (var candidate in entry.Slots)
            {
                if ((flags & candidate) != 0)
                    return entry.Parts;
            }
        }

        return null;
    }

    private static bool IsCovered(
        List<(HashSet<BodyPartType> Parts, HashSet<BodyPartSymmetry>? Sides)> coverage,
        BodyPartType type,
        BodyPartSymmetry symmetry)
    {
        foreach (var (parts, sides) in coverage)
        {
            if (parts.Contains(type) && (sides is not { Count: > 0 } || sides.Contains(symmetry)))
                return true;
        }

        return false;
    }

    /// <summary>The part's name in the lower case a sentence wants, falling back to the entity's own.</summary>
    private string PartName(EntityUid part, BodyPartComponent component)
    {
        if (_body.GetTargetBodyPart(component) is not { } target)
            return Name(part);

        var key = "wolfmed-look-part-name-" + PartKey(target);
        var name = Loc.GetString(key);
        return name == key ? Name(part) : name;
    }

    private static string PartKey(TargetBodyPart part) => part.ToString()
        .Replace("Left", "left-")
        .Replace("Right", "right-")
        .ToLowerInvariant();

    /// <summary>Head to foot, the order somebody's eye travels down a body.</summary>
    private static int PartOrder(BodyPartType type) => type switch
    {
        BodyPartType.Head => 0,
        BodyPartType.Torso => 1,
        BodyPartType.Arm => 2,
        BodyPartType.Hand => 3,
        BodyPartType.Leg => 4,
        BodyPartType.Foot => 5,
        BodyPartType.Tail => 6,
        _ => 7,
    };
}
