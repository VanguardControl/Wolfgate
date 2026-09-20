#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Examine;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// LOOK: the health examine read as a visual inspection. What shows is what an examiner could see on the
/// body in front of them: bare skin reveals everything, clothing hides all but gross findings, and the
/// patient knows their own body whatever they are wearing.
/// </summary>
/// <remarks>
/// Appearance is data (<c>_WF/Wolfmed/Wounds/look.yml</c>), so the last two tests are the ones that keep
/// it honest: every wound prototype declares what it looks like, and every key it names resolves.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedVisualInspectionSystem))]
public sealed class WolfmedVisualInspectionTest : GameTest
{
    /// <summary>Keys the system builds itself rather than reading off a look prototype.</summary>
    private static readonly string[] BuiltKeys =
    [
        "wolfmed-look-title-self", "wolfmed-look-title-other", "wolfmed-look-part-self",
        "wolfmed-look-part-other", "wolfmed-look-none-self", "wolfmed-look-none-other",
        "wolfmed-look-covered", "wolfmed-look-hidden", "wolfmed-look-distant", "wolfmed-look-sepsis-self",
        "wolfmed-look-sepsis-other", "wolfmed-look-scars", "wolfmed-look-numb",
        "wolfmed-look-tourniquet", "wolfmed-look-soaking", "wolfmed-look-soaking-mechanical",
        "wolfmed-look-part-name-head", "wolfmed-look-part-name-torso", "wolfmed-look-part-name-groin",
        "wolfmed-look-part-name-left-arm", "wolfmed-look-part-name-left-hand",
        "wolfmed-look-part-name-right-arm", "wolfmed-look-part-name-right-hand",
        "wolfmed-look-part-name-left-leg", "wolfmed-look-part-name-left-foot",
        "wolfmed-look-part-name-right-leg", "wolfmed-look-part-name-right-foot",
    ];

    /// <summary>A cut is there for anyone to see, until a sleeve goes over it. The owner still knows.</summary>
    [Test]
    public async Task BareSkinShowsWhatClothingHidesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Wound(entities, arm, "SlashWound", 30);

            var cut = Text(locale, "wolfmed-look-cut-moderate");
            var partName = Text(locale, "wolfmed-look-part-name-left-arm");

            Assert.Multiple(() =>
            {
                Assert.That(Look(entities, body, medic), Does.Contain(cut).And.Contain(partName),
                    "a cut on a bare arm is there to be seen.");
                Assert.That(Look(entities, body, body), Does.Contain(cut));
            });

            var uniform = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(body, uniform, "jumpsuit", true, true), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(Look(entities, body, medic), Does.Not.Contain(cut),
                    "a sleeve hides it from everyone else,");
                Assert.That(Look(entities, body, medic), Does.Contain(Text(locale, "wolfmed-look-covered")),
                    "and says so once rather than part by part.");
                Assert.That(Look(entities, body, body), Does.Contain(cut),
                    "but you can look under your own sleeve.");
            });
        });
    }

    /// <summary>Enough blood and the clothing stops hiding anything.</summary>
    [Test]
    public async Task HeavyBleedingSoaksThroughClothingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Bleed(entities, Wound(entities, arm, "SlashWound", 30), 2f);

            var uniform = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(body, uniform, "jumpsuit", true, true), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(Look(entities, body, medic), Does.Contain(Text(locale, "wolfmed-look-soaking")));
                Assert.That(Look(entities, body, body), Does.Contain(Text(locale, "wolfmed-look-bleed-flowing")),
                    "the owner sees the wound itself rather than the stain.");
            });
        });
    }

    /// <summary>A bone out of line shows through a jumpsuit. A crack in one shows to nobody.</summary>
    [Test]
    public async Task OnlyDeformityShowsThroughClothingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var uniform = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(body, uniform, "jumpsuit", true, true), Is.True);

            // Hairline: stage 12 in BoneFractureWound's own table.
            Wound(entities, arm, "BoneFractureWound", 12);
            var ache = Text(locale, "wolfmed-look-fracture-ache");
            var bent = Text(locale, "wolfmed-look-fracture-displaced");

            Assert.Multiple(() =>
            {
                Assert.That(Look(entities, body, medic), Does.Not.Contain(ache).And.Not.Contain(bent),
                    "a hairline fracture is a finding for the analyzer, not for the eye.");
                Assert.That(Look(entities, body, body), Does.Contain(ache),
                    "the patient gets an ache, not a diagnosis.");
                Assert.That(Look(entities, body, body), Does.Not.Contain(bent));
            });

            // 12 + 20 merges to 32, which is Displaced.
            Wound(entities, arm, "BoneFractureWound", 20);

            Assert.Multiple(() =>
            {
                Assert.That(Look(entities, body, medic), Does.Contain(bent),
                    "a limb bent the wrong way shows through a sleeve,");
                Assert.That(Look(entities, body, medic, false), Does.Contain(bent),
                    "and from across the room.");
            });
        });
    }

    /// <summary>What is under the skin stays under it, however bad it is.</summary>
    [Test]
    public async Task InternalFindingsNeverShowTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);
            var head = Part(entities, body, BodyPartType.Head, BodyPartSymmetry.None);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            Wound(entities, torso, "InternalBleedingWound", 30);
            Wound(entities, torso, "WolfmedOrganContusionWound", 20);
            Wound(entities, head, "WolfmedConcussionWound", 5);
            Wound(entities, arm, "WolfmedTendonCutWound", 20);

            var seen = Look(entities, body, medic);
            var known = Look(entities, body, body);

            Assert.Multiple(() =>
            {
                Assert.That(seen, Does.Contain(
                        Text(locale, "wolfmed-look-none-other", ("target", Identity.Entity(body, entities)))),
                    "a torso full of internal bleeding looks like an unhurt torso.");
                Assert.That(known, Does.Contain(Text(locale, "wolfmed-look-tendon-self")),
                    "the patient feels the tendon,");
                Assert.That(known, Does.Contain(Text(locale, "wolfmed-look-concussion-self")),
                    "and the concussion,");
                Assert.That(known, Does.Not.Contain(Text(locale, "wolfmed-look-concussion-severe")),
                    "without the unfocused eyes a bad one would show.");
            });
        });
    }

    /// <summary>A dressing, a splint and a tourniquet are as visible as the injury under them.</summary>
    [Test]
    public async Task TreatmentIsVisibleTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);

            var stitched = Bleed(entities, Wound(entities, arm, "SlashWound", 30), 6f);
            entities.GetComponent<WoundBleedingComponent>(stitched).Treatment = BleedingTreatment.Sutured;
            entities.EnsureComponent<WolfmedTourniquetComponent>(arm);

            // Comminuted at 60 Blunt, which is what the splint needs before it will go on.
            entities.System<DamageableSystem>()
                .TryChangeDamage(body, Blunt(60), origin: null, targetPart: TargetBodyPart.LeftLeg);
            var splint = entities.SpawnEntity("WolfmedSplint", map.GridCoords);
            Assert.That(entities.System<WolfmedSplintSystem>()
                .TryApply((splint, entities.GetComponent<WolfmedSplintComponent>(splint)), body, leg, medic), Is.True);

            var treated = Line(entities, locale, body, medic, "left-arm");

            Assert.Multiple(() =>
            {
                Assert.That(treated, Does.Contain(Text(locale, "wolfmed-look-treatment-sutured")));
                Assert.That(treated, Does.Not.Contain(Text(locale, "wolfmed-look-bleed-spurting")),
                    "a closed wound does not read as still bleeding.");
                Assert.That(treated, Does.Contain(Text(locale, "wolfmed-look-tourniquet")));
                Assert.That(Line(entities, locale, body, medic, "left-leg"),
                    Does.Contain(Text(locale, "wolfmed-look-splint-splint")));
            });
        });
    }

    /// <summary>A round still in the wound is a hole. Shrapnel is metal you can see.</summary>
    [Test]
    public async Task EmbeddedObjectsReadDifferentlyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            Wound(entities, Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left),
                "WolfmedLodgedRoundWound", 10);
            Wound(entities, Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Right),
                "WolfmedShrapnelWound", 10);

            var seen = Look(entities, body, medic);

            Assert.Multiple(() =>
            {
                Assert.That(seen, Does.Contain(Text(locale, "wolfmed-look-lodged-round")),
                    "a lodged round reads as a hole with nothing coming back out of it.");
                Assert.That(seen, Does.Contain(Text(locale, "wolfmed-look-shrapnel-minor")),
                    "shrapnel stands out of the skin.");
            });
        });
    }

    /// <summary>A chassis dents, breaches and leaks. It never bruises or bleeds.</summary>
    [Test]
    public async Task ChassisUsesMechanicalWordingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var machine = entities.SpawnEntity("MobIPC", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, machine, BodyPartType.Arm, BodyPartSymmetry.Left);
            Bleed(entities, Wound(entities, arm, "WolfmedBreachWound", 10), 2f);

            var seen = Look(entities, machine, medic);

            Assert.Multiple(() =>
            {
                Assert.That(seen, Does.Contain(Text(locale, "wolfmed-look-breach-minor")));
                Assert.That(seen, Does.Contain(Text(locale, "wolfmed-look-bleed-flowing-mechanical")),
                    "a chassis leaks fluid,");
                Assert.That(seen, Does.Not.Contain(Text(locale, "wolfmed-look-bleed-flowing")),
                    "and never bleeds.");
            });
        });
    }

    /// <summary>From across the room only the gross things carry.</summary>
    [Test]
    public async Task DistanceHidesDetailTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            Wound(entities, Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left), "SlashWound", 30);
            Wound(entities, Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Right),
                "WolfmedDislocationWound", 10);

            var far = Look(entities, body, medic, false);

            Assert.Multiple(() =>
            {
                Assert.That(far, Does.Contain(Text(locale, "wolfmed-look-dislocation")),
                    "a joint out of its socket is visible from a distance,");
                Assert.That(far, Does.Not.Contain(Text(locale, "wolfmed-look-cut-moderate")),
                    "a cut is not,");
                Assert.That(far, Does.Contain(Text(locale, "wolfmed-look-distant")),
                    "and the examiner is told why.");
                Assert.That(Look(entities, body, body, false),
                    Does.Contain(Text(locale, "wolfmed-look-cut-moderate")),
                    "your own body is never far away.");
            });
        });
    }

    /// <summary>Every wound in the game declares what it looks like, or it is invisible by accident.</summary>
    [Test]
    public async Task EveryWoundDeclaresALookTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var looks = entities.System<WolfmedVisualInspectionSystem>();
            var claimed = new Dictionary<string, string>();

            foreach (var look in prototypes.EnumeratePrototypes<WolfmedWoundLookPrototype>())
            {
                Assert.That(prototypes.HasIndex(look.Wound), Is.True,
                    $"{look.ID} describes a wound that does not exist.");
                Assert.That(claimed.TryAdd(look.Wound.Id, look.ID), Is.True,
                    $"{look.ID} and {claimed.GetValueOrDefault(look.Wound.Id)} both describe {look.Wound.Id}.");
            }

            foreach (var wound in prototypes.EnumeratePrototypes<WoundPrototype>())
            {
                Assert.That(looks.TryGetLook(wound.ID, out _), Is.True,
                    $"{wound.ID} has no wolfmedWoundLook: nobody could ever see it.");
            }
        });
    }

    /// <summary>Every string the inspection can print resolves, including every stage of every wound.</summary>
    [Test]
    public async Task EveryLookStringResolvesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var keys = new List<string>(BuiltKeys);

            foreach (var treatment in new[] { "bandaged", "clamped", "sutured", "cauterized" })
                keys.Add("wolfmed-look-treatment-" + treatment);

            foreach (var overlay in new[] { "gauze", "splint", "splintimprovised", "splinttribal" })
                keys.Add("wolfmed-look-splint-" + overlay);

            foreach (var stage in new[] { "local", "spreading" })
                keys.Add("wolfmed-look-infection-" + stage);

            foreach (var band in new[] { "oozing", "flowing", "spurting" })
            {
                keys.Add("wolfmed-look-bleed-" + band);
                keys.Add("wolfmed-look-bleed-" + band + "-mechanical");
            }

            foreach (var look in prototypes.EnumeratePrototypes<WolfmedWoundLookPrototype>())
            {
                Collect(keys, look.Description, look.SelfHint);
                foreach (var stage in look.Stages.Values)
                    Collect(keys, stage.Description, stage.SelfHint);

                // A stage the wound itself does not declare would never be reached.
                var wound = prototypes.Index(look.Wound);
                foreach (var stage in look.Stages.Keys)
                {
                    Assert.That(wound.Stages.ContainsKey(stage), Is.True,
                        $"{look.ID} describes stage {stage}, which {wound.ID} does not have.");
                }
            }

            foreach (var key in keys)
                Assert.That(locale.HasString(key), Is.True, $"{key} has no string.");
        });
    }

    private static void Collect(List<string> keys, LocId? description, LocId? hint)
    {
        if (description is { } value)
            keys.Add(value.Id);

        if (hint is { } self)
            keys.Add(self.Id);
    }

    private static string Look(IEntityManager entities, EntityUid examined, EntityUid examiner, bool detailed = true)
    {
        var message = new FormattedMessage();
        entities.System<WolfmedVisualInspectionSystem>().AddLookMarkup(examined, examiner, message, detailed);
        return message.ToString();
    }

    /// <summary>The one line the inspection prints for a part, so a finding elsewhere cannot answer for it.</summary>
    private static string Line(
        IEntityManager entities,
        ILocalizationManager locale,
        EntityUid examined,
        EntityUid examiner,
        string part)
    {
        var name = Text(locale, "wolfmed-look-part-name-" + part);
        foreach (var line in Look(entities, examined, examiner).Split('\n'))
        {
            if (line.Contains(name))
                return line;
        }

        return string.Empty;
    }

    private static string Text(ILocalizationManager locale, string key, params (string, object)[] args) =>
        FormattedMessage.RemoveMarkupPermissive(locale.GetString(key, args));

    private static EntityUid Wound(IEntityManager entities, EntityUid part, string prototype, int severity)
    {
        var wound = entities.System<WoundSystem>().CreateOrMergeWound(part, prototype, FixedPoint2.New(severity));
        Assert.That(wound, Is.Not.Null, $"{prototype} could not be created on that part.");
        return wound!.Value;
    }

    /// <summary>Pins a bleeding rate rather than waiting on the server's own roll and clotting tick.</summary>
    private static EntityUid Bleed(IEntityManager entities, EntityUid wound, float rate)
    {
        entities.EnsureComponent<WoundBleedingComponent>(wound).CurrentRate = rate;
        return wound;
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type, BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static DamageSpecifier Blunt(int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>("Blunt")] = FixedPoint2.New(amount) },
    };
}
