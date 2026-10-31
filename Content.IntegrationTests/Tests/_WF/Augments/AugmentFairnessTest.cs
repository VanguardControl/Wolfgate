using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Part;
using Content.Shared._WF.Augments;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Cloning;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>
/// Free augment limbs give no edge over what they replace: they feel pain and bleed in full, a chassis limb gets no
/// sturdier, a clone wears the real limbs and a limb replaced since is not drawn over.
/// </summary>
[TestOf(typeof(WFAugmentRules))]
public sealed class AugmentFairnessTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private static HumanoidCharacterProfile RightArm(string species = "Human")
    {
        return HumanoidCharacterProfile.DefaultWithSpecies(species).WithAugments(new Dictionary<WFAugmentSlot, WFAugmentChoice>
        {
            [WFAugmentSlot.RightArm] = new("WFAugmentCyberRightArm", "WFAugmentStyleHephaestus"),
        });
    }

    private EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry)
    {
        return SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, type, symmetry: symmetry).Single().Id;
    }

    [Test]
    public async Task LimbsFeelPainAndBleedInFullTest()
    {
        var protos = Server.ResolveDependency<IPrototypeManager>();
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugments(new Dictionary<WFAugmentSlot, WFAugmentChoice>
        {
            [WFAugmentSlot.LeftArm] = new("WFAugmentCyberLeftArm", "WFAugmentStyleHephaestus"),
            [WFAugmentSlot.RightArm] = new("WFAugmentCyberRightArm", "WFAugmentStyleHephaestus"),
            [WFAugmentSlot.LeftLeg] = new("WFAugmentCyberLeftLeg", "WFAugmentStyleHephaestus"),
            [WFAugmentSlot.RightLeg] = new("WFAugmentCyberRightLeg", "WFAugmentStyleHephaestus"),
        });

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(SPlayer, profile);
            var pain = SEntMan.System<PainSystem>();
            var flesh = protos.Index<BodyPartProfilePrototype>("OrganicBodyPartProfile");
            var blunt = new DamageSpecifier(protos.Index<DamageTypePrototype>("Blunt"), 20);

            foreach (var (type, symmetry) in new[]
                     {
                         (BodyPartType.Arm, BodyPartSymmetry.Left), (BodyPartType.Hand, BodyPartSymmetry.Left),
                         (BodyPartType.Arm, BodyPartSymmetry.Right), (BodyPartType.Hand, BodyPartSymmetry.Right),
                         (BodyPartType.Leg, BodyPartSymmetry.Left), (BodyPartType.Foot, BodyPartSymmetry.Left),
                         (BodyPartType.Leg, BodyPartSymmetry.Right), (BodyPartType.Foot, BodyPartSymmetry.Right),
                     })
            {
                var part = Part(SPlayer, type, symmetry);
                var label = $"{symmetry} {type}";
                var woundable = SEntMan.GetComponent<WoundableComponent>(part);
                Assert.That(woundable.Profile.Id, Is.EqualTo("WFAugmentBodyPartProfile"), label);
                Assert.That(protos.Index(woundable.Profile).BleedingMultiplier, Is.EqualTo(flesh.BleedingMultiplier), label);
                Assert.That(pain.CanFeelPain(part), label);

                SEntMan.EnsureComponent<PainComponent>(part);
                Assert.That(pain.ApplyDamage(part, blunt), label + " took damage and felt nothing.");
            }
        });
    }

    [Test]
    public async Task ChassisLimbGetsNoSturdierTest()
    {
        var protos = Server.ResolveDependency<IPrototypeManager>();
        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var ipc = SEntMan.SpawnEntity(protos.Index<SpeciesPrototype>("IPC").Prototype, coords);
            var thresholds = new Dictionary<BodyPartType, Dictionary<ProtoId<DamageTypePrototype>, Content.Shared.FixedPoint.FixedPoint2>>();
            foreach (var type in new[] { BodyPartType.Arm, BodyPartType.Hand })
                thresholds[type] = new(SEntMan.GetComponent<WolfmedBodyPartComponent>(Part(ipc, type, BodyPartSymmetry.Right)).AmputationThresholds);

            var human = SEntMan.GetComponent<WolfmedBodyPartComponent>(Part(SPlayer, BodyPartType.Arm, BodyPartSymmetry.Right)).AmputationThresholds;
            Assert.That(thresholds[BodyPartType.Arm], Is.Not.EquivalentTo(human), "This test needs a chassis limb that differs from flesh.");

            SEntMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(ipc, RightArm("IPC"));

            foreach (var (type, expected) in thresholds)
            {
                var part = Part(ipc, type, BodyPartSymmetry.Right);
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(part).EntityPrototype?.ID, Does.StartWith("WFAugment"));
                Assert.That(SEntMan.GetComponent<WolfmedBodyPartComponent>(part).AmputationThresholds, Is.EquivalentTo(expected), type.ToString());
            }

            SEntMan.DeleteEntity(ipc);
        });
    }

    [Test]
    public async Task CloneWearsTheRealLimbsTest()
    {
        await Server.WaitAssertion(() =>
        {
            var humanoids = SEntMan.System<SharedHumanoidAppearanceSystem>();
            humanoids.LoadProfile(SPlayer, RightArm());

            var clone = SEntMan.SpawnEntity("MobHuman", SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            humanoids.CloneAppearance(SPlayer, clone);
            var ev = new CloningEvent(SPlayer, clone);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, ref ev);

            var arm = Part(clone, BodyPartType.Arm, BodyPartSymmetry.Right);
            var hand = Part(clone, BodyPartType.Hand, BodyPartSymmetry.Right);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(arm).EntityPrototype?.ID, Is.EqualTo("WFAugmentRightArm"));
            Assert.That(SEntMan.GetComponent<BodyPartAppearanceComponent>(arm).ID?.Id, Is.EqualTo("WFMobAugmentHephaestusRArm"));
            Assert.That(SEntMan.GetComponent<BodyPartAppearanceComponent>(hand).ID?.Id, Is.EqualTo("WFMobAugmentHephaestusRHand"));

            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(clone);
            Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.RArm].Id?.Id, Is.EqualTo("WFMobAugmentHephaestusRArm"));
            Assert.That(humanoid.AugmentLayers, Does.ContainKey(HumanoidVisualLayers.RArm));

            SEntMan.DeleteEntity(clone);
        });
    }

    [Test]
    public async Task ReplacedLimbIsNotDrawnOverTest()
    {
        await Server.WaitAssertion(() =>
        {
            var humanoids = SEntMan.System<SharedHumanoidAppearanceSystem>();
            var bodies = SEntMan.System<SharedBodySystem>();
            humanoids.LoadProfile(SPlayer, RightArm());

            var body = SEntMan.GetComponent<BodyComponent>(SPlayer);
            var torso = bodies.GetBodyChildrenOfType(SPlayer, BodyPartType.Torso).Single();
            bodies.WfCutLoose(SPlayer, Part(SPlayer, BodyPartType.Arm, BodyPartSymmetry.Right), body);
            var flesh = SEntMan.SpawnEntity("RightArmHuman", SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            Assert.That(bodies.AttachPart(torso.Id, "right arm", flesh, torso.Component, SEntMan.GetComponent<BodyPartComponent>(flesh)));

            // A reload that doesn't pick the slot, as a DNA scrambler does.
            humanoids.LoadProfile(SPlayer, HumanoidCharacterProfile.DefaultWithSpecies("Human"));

            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(SPlayer);
            Assert.That(humanoid.AugmentLayers, Does.Not.ContainKey(HumanoidVisualLayers.RArm));
            Assert.That(humanoid.CustomBaseLayers.ContainsKey(HumanoidVisualLayers.RArm), Is.False, "The old augment was drawn over the flesh arm.");
        });
    }
}
