using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
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
using Content.Shared.Item.ItemToggle;
using Content.Shared.Tools.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

[TestFixture]
public sealed class WolfmedWeldingRepairTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestCase("MobHuman", "SpeedLeftLeg", TargetBodyPart.LeftLeg)]
    [TestCase("MobHuman", "SpeedRightLeg", TargetBodyPart.RightLeg)]
    [TestCase("MobIPC", null, TargetBodyPart.LeftLeg)]
    [TestCase("MobHuman", null, TargetBodyPart.LeftLeg)]
    public async Task WelderRepairsSelectedMechanicalLimbTest(string prototype, string replacement, TargetBodyPart target)
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default, part = default, tool = default;
        var mechanical = replacement != null || prototype == "MobIPC";
        var fuelBefore = 0f;
        Content.Shared.DoAfter.DoAfter repair = null;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity(prototype, map.GridCoords);
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.RemoveComponent<BarotraumaComponent>(body);
            entities.RemoveComponent<BarotraumaComponent>(user);
            entities.RemoveComponent<TemperatureComponent>(body);
            entities.RemoveComponent<TemperatureComponent>(user);
            var graph = entities.System<SharedBodySystem>();
            var (type, symmetry) = graph.ConvertTargetBodyPart(target);
            part = graph.GetBodyChildrenOfType(body, type, symmetry: symmetry).Single().Id;
            if (replacement != null)
            {
                var torso = graph.GetBodyChildrenOfType(body, BodyPartType.Torso).Single().Id;
                Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(part), Is.True);
                part = entities.SpawnEntity(replacement, map.GridCoords);
                var slot = target == TargetBodyPart.LeftLeg ? "left leg" : "right leg";
                Assert.That(graph.AttachPart(torso, slot, part), Is.True);
            }

            var slash = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Slash");
            Assert.That(entities.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, part, new DamageSpecifier(slash, 15), ignoreResistances: true), Is.True);
            Assert.That(entities.System<WoundSystem>().GetWounds(part).Any(), Is.True);
            if (mechanical)
            {
                entities.System<DamageableSystem>().TryChangeDamage(body, new DamageSpecifier(slash, -5),
                    true, false, origin: body, targetPart: target,
                    originFlag: DamageableSystem.DamageOriginFlag.PassiveRecovery);
                Assert.That(entities.GetComponent<DamageableComponent>(part).TotalDamage.Float(), Is.EqualTo(15f),
                    "Mechanical limbs must still reject passive regeneration while accepting tool repair.");
            }
            if (replacement != null)
                Assert.That(entities.GetComponent<BloodstreamComponent>(body).BleedAmount, Is.GreaterThan(0f));

            tool = entities.SpawnEntity("Welder", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            Assert.That(entities.System<ItemToggleSystem>().TryActivate(tool, user), Is.True);
            entities.GetComponent<TargetingComponent>(user).Target = target;
            fuelBefore = entities.System<SharedToolSystem>().GetWelderFuelAndCapacity(tool).fuel.Float();
            var interact = new InteractUsingEvent(user, tool, body, map.GridCoords);
            entities.EventBus.RaiseLocalEvent(body, interact);
            if (mechanical)
            {
                Assert.That(interact.Handled, Is.True);
                repair = entities.GetComponent<DoAfterComponent>(user).DoAfters.Values.Single();
            }
            // Changing the aiming selector must not redirect an in-progress repair onto flesh.
            entities.GetComponent<TargetingComponent>(user).Target = TargetBodyPart.Torso;
        });

        await Pair.RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>().GetWounds(part).ToList();
            if (!mechanical)
            {
                Assert.That(wounds, Is.Not.Empty, "Welding must not heal an organic leg.");
                return;
            }

            Assert.That(repair.Cancelled, Is.False, "The test repair was interrupted before it could finish.");
            Assert.That(repair.Completed, Is.True, $"Repair delay: {repair.Args.Delay}");
            Assert.That(wounds, Is.Empty, "The actual tool interaction must close the mechanical wound.");
            Assert.That(entities.GetComponent<BloodstreamComponent>(body).BleedAmount, Is.Zero);
            Assert.That(entities.GetComponent<DamageableComponent>(part).TotalDamage.Float(), Is.Zero);
            Assert.That(entities.System<SharedToolSystem>().GetWelderFuelAndCapacity(tool).fuel.Float(),
                Is.LessThanOrEqualTo(fuelBefore - 5), "A successful repair must consume its fuel cost.");
        });
    }
}
