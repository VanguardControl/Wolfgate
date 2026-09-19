#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Medical.Components;
using Content.Server.Speech.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W3: what a heavy blunt hit leaves behind - a crushed limb, a rattled head, a popped joint, a bruised
/// organ - and what each of them takes to clear.
/// </summary>
/// <remarks>
/// The wounds and their thresholds are data (<c>_WF/Wolfmed/Wounds/blunt.yml</c> plus their rules). The new
/// behaviour is <see cref="WolfmedConcussionSystem"/>, <see cref="WolfmedDislocationSystem"/> and
/// <see cref="WolfmedOrganContusionSystem"/>, all three fed by the
/// <see cref="WolfmedWoundLifecycleEvent"/> relay. Effects are asserted off freshly created wounds rather
/// than off damage wherever a fracture roll (25-100 % between 12 and 45 Blunt) would shadow them.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedConcussionSystem))]
public sealed class WolfmedBluntWoundTest : GameTest
{
    /// <summary>
    /// A heavy swing crushes the limb in place of the ordinary bruise and pops the joint on the way; a
    /// light one does neither.
    /// </summary>
    [Test]
    public async Task CrushInjuryReplacesTheBruiseTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var traits = entities.System<WolfmedWoundTraitSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            Blunt(entities, body, TargetBodyPart.LeftArm, 35);
            Blunt(entities, body, TargetBodyPart.RightArm, 8);

            var crushed = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var bruised = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Right);

            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, crushed), Does.Contain("WolfmedCrushInjuryWound")
                        .And.Not.Contain("BluntWound"),
                    "at this size the bruise IS the crush injury, so the rule replaces it.");
                Assert.That(Prototypes(entities, wounds, crushed), Does.Contain("WolfmedDislocationWound"),
                    "`continue` lets the same swing pop the joint.");
                Assert.That(Prototypes(entities, wounds, bruised), Does.Contain("BluntWound")
                        .And.Not.Contain("WolfmedCrushInjuryWound")
                        .And.Not.Contain("WolfmedDislocationWound"),
                    "a light hit is still just a bruise.");
                Assert.That(traits.TryGetLimbPenalty(crushed, true, out _), Is.True,
                    "a crushed limb costs its use, through the same modifier a fracture uses.");
            });

            var crush = FindWound(entities, wounds, crushed, "WolfmedCrushInjuryWound");
            Assert.That(entities.GetComponent<WoundComponent>(crush).Severity, Is.EqualTo(FixedPoint2.New(35)));
        });
    }

    /// <summary>
    /// A crushing blow can start a bleed nothing external will reach. The one chance roll in W3, so this
    /// takes enough swings that a run of bad luck is not a plausible failure.
    /// </summary>
    [Test]
    public async Task CrushCanBleedInternallyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var bled = 0;

            for (var i = 0; i < 8; i++)
            {
                var body = entities.SpawnEntity("MobHuman", map.GridCoords);
                for (var hit = 0; hit < 3; hit++)
                    Blunt(entities, body, TargetBodyPart.Torso, 35);

                var torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);
                Assert.That(Prototypes(entities, wounds, torso), Does.Contain("WolfmedCrushInjuryWound"));
                if (Prototypes(entities, wounds, torso).Contains("InternalBleedingWound"))
                    bled++;
            }

            Assert.That(bled, Is.GreaterThan(0), "24 crushing blows without one internal bleed at 40 %.");
        });
    }

    /// <summary>
    /// A knock to the head blurs sight, slurs speech and drops the patient; no item touches it, it fades
    /// on its own, and it fades four times faster in bed.
    /// </summary>
    [Test]
    public async Task ConcussionFadesOnlyWithTimeTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var concussions = entities.System<WolfmedConcussionSystem>();
            var healing = entities.System<WoundHealingSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.Head, 25);

            var head = Part(entities, body, BodyPartType.Head, BodyPartSymmetry.None);
            var concussion = FindWound(entities, wounds, head, "WolfmedConcussionWound");

            Assert.Multiple(() =>
            {
                // severityMultiplier 0.6: 25 Blunt is a moderate concussion.
                Assert.That(entities.GetComponent<WoundComponent>(concussion).Severity,
                    Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(entities.TryGetComponent(body, out WolfmedConcussionComponent? state), Is.True);
                Assert.That(state!.Blur, Is.EqualTo(3f), "the moderate stage's blur.");
                Assert.That(state.Stutter, Is.True);
                Assert.That(entities.GetComponent<BlurryVisionComponent>(body).Magnitude, Is.GreaterThanOrEqualTo(3f));
                Assert.That(entities.HasComponent<StutteringAccentComponent>(body), Is.True);
                Assert.That(entities.HasComponent<KnockedDownComponent>(body), Is.True,
                    "the blow itself puts them on the floor.");
            });

            // Nothing in a medkit is a cure; the wound carries no damage types to heal.
            var brutepack = entities.SpawnEntity("BrutepackAdvanced1", map.GridCoords);
            healing.TryApplyHealing(body, head,
                (brutepack, entities.GetComponent<HealingComponent>(brutepack)), body, out _, out _);
            Assert.That(entities.GetComponent<WoundComponent>(concussion).Severity, Is.EqualTo(FixedPoint2.New(15)));

            // A minute on their feet is worth two severity, and drops the stage back to minor.
            concussions.Recover(body, 60f);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundComponent>(concussion).Severity,
                    Is.EqualTo(FixedPoint2.New(13)));
                Assert.That(entities.GetComponent<WolfmedConcussionComponent>(body).Blur, Is.EqualTo(1.5f));
                Assert.That(entities.GetComponent<WolfmedConcussionComponent>(body).Stutter, Is.False,
                    "the slur belongs to the moderate stage and up.");
            });

            // The same minute asleep is worth four times as much.
            entities.AddComponent<SleepingComponent>(body);
            Assert.That(concussions.IsResting(body), Is.True);
            concussions.Recover(body, 60f);
            Assert.That(entities.GetComponent<WoundComponent>(concussion).Severity, Is.EqualTo(FixedPoint2.New(5)));

            // The wound entity itself is only queued for deletion here, and the blur overlay is removed
            // deferred; what is immediate is that the part no longer carries it and the body is clear.
            concussions.Recover(body, 600f);
            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, head), Does.Not.Contain("WolfmedConcussionWound"),
                    "it heals all the way out.");
                Assert.That(entities.HasComponent<WolfmedConcussionComponent>(body), Is.False);
            });
        });
    }

    /// <summary>
    /// A dislocated joint costs the limb its use, refuses every treatment, and comes back only when
    /// someone sets it by hand.
    /// </summary>
    [Test]
    public async Task DislocationIsSetByHandTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var traits = entities.System<WolfmedWoundTraitSystem>();
            var joints = entities.System<WolfmedDislocationSystem>();
            var healing = entities.System<WoundHealingSystem>();
            var verbs = entities.System<SharedVerbSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 25);

            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var dislocation = FindWound(entities, wounds, leg, "WolfmedDislocationWound");

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundComponent>(dislocation).Severity,
                    Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(traits.TryGetLimbPenalty(leg, true, out var modifier), Is.True);
                Assert.That(modifier, Is.EqualTo(0.65f), "a popped joint limps like a broken one.");
                Assert.That(joints.FindDislocation(body, medic)?.Owner, Is.EqualTo(dislocation));
                Assert.That(verbs.GetLocalVerbs(body, medic, typeof(AlternativeVerb))
                        .Any(verb => verb.Text == Loc.GetString("wolfmed-relocate-verb")), Is.True,
                    "the medic is offered the relocate verb on the patient.");
            });

            foreach (var item in new[] { "Gauze1", "BrutepackAdvanced1" })
            {
                var topical = entities.SpawnEntity(item, map.GridCoords);
                healing.TryApplyHealing(body, leg,
                    (topical, entities.GetComponent<HealingComponent>(topical)), body, out _, out _);
            }

            Assert.That(entities.GetComponent<WoundComponent>(dislocation).Severity,
                Is.EqualTo(FixedPoint2.New(10)), "no damage type means no topical can reach it.");

            Assert.That(joints.TryRelocate(body, dislocation, medic), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, leg), Does.Not.Contain("WolfmedDislocationWound"));
                Assert.That(traits.TryGetLimbPenalty(leg, true, out _), Is.False, "and the limp goes with it.");
                Assert.That(joints.FindDislocation(body, medic), Is.Null);
            });

            // Doing it to yourself is the same procedure, slower and twice as painful.
            var prototype = prototypes.Index<WoundPrototype>("WolfmedDislocationWound");
            Assert.That(prototype.TryGetBehavior(FixedPoint2.New(10), out WolfmedDislocationBehavior behavior));
            Assert.Multiple(() =>
            {
                Assert.That(behavior.SelfMultiplier, Is.GreaterThan(1f));
                Assert.That(behavior.SelfPainMultiplier, Is.GreaterThan(1f));
                Assert.That(behavior.Pain, Is.GreaterThan(FixedPoint2.Zero));
            });
        });
    }

    /// <summary>A blow to the chest bruises an organ and never finishes one off.</summary>
    [Test]
    public async Task OrganContusionBruisesWithoutDestroyingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var contusions = entities.System<WolfmedOrganContusionSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);
            var before = OrganHealth(entities, torso);

            Blunt(entities, body, TargetBodyPart.Torso, 25);

            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, torso), Does.Contain("WolfmedOrganContusionWound"));
                Assert.That(OrganHealth(entities, torso), Is.LessThan(before),
                    "the blow reached something inside the chest.");
                Assert.That(Organs(entities, torso).All(organ => organ.Comp.Health > FixedPoint2.Zero), Is.True,
                    "and left all of it working.");
            });

            // The clamp, at a size no hit would ever deal: a contusion bruises, it never destroys.
            var bruised = contusions.TryBruise(torso, FixedPoint2.New(1000), FixedPoint2.New(1));
            Assert.That(bruised, Is.Not.Null);
            Assert.That(entities.GetComponent<WolfmedOrganComponent>(bruised!.Value).Health,
                Is.EqualTo(FixedPoint2.New(1)));
        });
    }

    /// <summary>The analyzer names the four wounds and the guide has words for them.</summary>
    [Test]
    public async Task NamesExistTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.System<Content.Server.Medical.HealthAnalyzerSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftArm, 35);

            var diagnostics = analyzer.BuildWoundDiagnostics(body);
            Assert.That(diagnostics, Is.Not.Null);
            Assert.That(diagnostics!.Parts.TryGetValue(TargetBodyPart.LeftArm, out var arm));

            Assert.Multiple(() =>
            {
                Assert.That(arm.VisibleWounds.Any(wound => wound.Name == "wolfmed-wound-name-crush-injury"));
                Assert.That(locale.HasString("wolfmed-wound-name-crush-injury"));
                Assert.That(locale.HasString("wolfmed-wound-name-concussion"));
                Assert.That(locale.HasString("wolfmed-wound-name-dislocation"));
                Assert.That(locale.HasString("wolfmed-wound-name-organ-contusion"));
                Assert.That(locale.HasString("wolfmed-relocate-verb"));
                Assert.That(locale.HasString("wolfmed-relocate-start"));
                Assert.That(locale.HasString("wolfmed-relocate-start-self"));
                Assert.That(locale.HasString("wolfmed-relocate-success"));
            });
        });
    }

    private static void Blunt(IEntityManager entities, EntityUid body, TargetBodyPart target, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec("Blunt", amount),
            origin: null, targetPart: target);
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type, BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static List<string> Prototypes(IEntityManager entities, WoundSystem wounds, EntityUid part)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Select(wound => wound.Comp.Prototype.Id)
            .ToList();
    }

    private static EntityUid FindWound(IEntityManager entities, WoundSystem wounds, EntityUid part, string prototype)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .First(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype))
            .Owner;
    }

    private static List<Entity<WolfmedOrganComponent>> Organs(IEntityManager entities, EntityUid part)
    {
        return entities.System<SharedBodySystem>().GetPartOrgans(part)
            .Where(organ => entities.HasComponent<WolfmedOrganComponent>(organ.Id))
            .Select(organ => new Entity<WolfmedOrganComponent>(organ.Id,
                entities.GetComponent<WolfmedOrganComponent>(organ.Id)))
            .ToList();
    }

    private static FixedPoint2 OrganHealth(IEntityManager entities, EntityUid part)
    {
        var total = FixedPoint2.Zero;
        foreach (var organ in Organs(entities, part))
            total += organ.Comp.Health;

        return total;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
