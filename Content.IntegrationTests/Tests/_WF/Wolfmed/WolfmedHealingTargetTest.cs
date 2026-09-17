using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.Components;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

[TestFixture]
public sealed class WolfmedHealingTargetTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestCase("Brutepack", "Slash", false)]
    [TestCase("Brutepack", "Slash", true)]
    [TestCase("CableApcStack", "Heat", false)]
    [TestCase("CableApcStack", "Heat", true)]
    public async Task TargetedItemsNeverHealAnotherLimbTest(string item, string damageType, bool missingPart)
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid left = default, right = default;

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var mob in new[] { body, user })
            {
                entities.RemoveComponent<BarotraumaComponent>(mob);
                entities.RemoveComponent<TemperatureComponent>(mob);
            }
            var graph = entities.System<SharedBodySystem>();
            var torso = graph.GetBodyChildrenOfType(body, BodyPartType.Torso).Single().Id;
            left = graph.GetBodyChildrenOfType(body, BodyPartType.Leg, symmetry: BodyPartSymmetry.Left).Single().Id;
            right = graph.GetBodyChildrenOfType(body, BodyPartType.Leg, symmetry: BodyPartSymmetry.Right).Single().Id;
            if (item == "CableApcStack")
            {
                Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(left), Is.True);
                Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(right), Is.True);
                left = entities.SpawnEntity("SpeedLeftLeg", map.GridCoords);
                right = entities.SpawnEntity("SpeedRightLeg", map.GridCoords);
                Assert.That(graph.AttachPart(torso, "left leg", left), Is.True);
                Assert.That(graph.AttachPart(torso, "right leg", right), Is.True);
            }
            var damage = new DamageSpecifier(server.ResolveDependency<IPrototypeManager>()
                .Index<DamageTypePrototype>(damageType), 6);
            var routing = entities.System<WoundDamageRoutingSystem>();
            Assert.That(routing.TryApplyPartDamage(body, left, damage, ignoreResistances: true), Is.True);
            Assert.That(routing.TryApplyPartDamage(body, right, damage, ignoreResistances: true), Is.True);
            if (missingPart)
                Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(left), Is.True);

            var medicine = entities.SpawnEntity(item, map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(user, medicine), Is.True);
            entities.GetComponent<TargetingComponent>(user).Target = TargetBodyPart.LeftLeg;
            var interact = new AfterInteractEvent(user, medicine, body, map.GridCoords, true);
            entities.EventBus.RaiseLocalEvent(medicine, interact);
            Assert.That(entities.GetComponent<DoAfterComponent>(user).DoAfters.Count,
                missingPart ? Is.Zero : Is.GreaterThan(0));
            entities.GetComponent<TargetingComponent>(user).Target = TargetBodyPart.RightLeg;
        });

        await Pair.RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            Assert.That(wounds.GetWounds(right).Sum(wound => wound.Comp.Severity.Float()), Is.EqualTo(6f),
                "Neither a missing target nor changing aim during treatment may heal the other leg.");
            if (!missingPart)
                Assert.That(wounds.GetWounds(left), Is.Empty, "The originally selected leg should be treated.");
        });
    }
}
