using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Server.Projectiles;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Checks explosive Pinhole shells cannot detonate on an active shield contact.</summary>
[TestFixture]
public sealed class WFShipShieldPinholeTest
{
    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    public async Task PhysicalPinholeContactAbsorbsOnlyShieldImpacts(bool consumeBeforeCollisionEvent, bool shieldContact)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var system = entities.System<ShipShieldsSystem>();
            var generator = entities.SpawnEntity("ShieldGenerator", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(generator);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var shield = emitter.Shield!.Value;
            var visuals = entities.GetComponent<WFShipShieldVisualsComponent>(shield);
            var coordinate = entities.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(shield, visuals.Contours[0][0]));
            var shell = entities.SpawnEntity("ShipPinholeProjectile", coordinate);
            var projectile = entities.GetComponent<ProjectileComponent>(shell);
            var explosive = entities.GetComponent<ExplosiveComponent>(shell);
            var body = entities.GetComponent<PhysicsComponent>(shell);
            var expectedDamage = (float) projectile.Damage.GetTotal() + explosive.TotalIntensity *
                (float) pair.Server.ResolveDependency<IPrototypeManager>().Index(explosive.ExplosionType).DamagePerIntensity.GetTotal();
            var target = shieldContact ? shield : entities.SpawnEntity("WallSolid", coordinate);
            if (consumeBeforeCollisionEvent)
                entities.System<ProjectileSystem>().ProjectileCollide((shell, projectile, body), shield);
            var fixture = entities.GetComponent<FixturesComponent>(shell).Fixtures.First();
            var shieldFixture = entities.GetComponent<FixturesComponent>(target).Fixtures.First();
            var constructor = typeof(StartCollideEvent).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var points = Activator.CreateInstance(constructor.GetParameters()[8].ParameterType)!;
            var collision = (StartCollideEvent) constructor.Invoke(new object[]
            {
                shell, target, fixture.Key, shieldFixture.Key, fixture.Value, shieldFixture.Value,
                body, entities.GetComponent<PhysicsComponent>(target), points, 0, Vector2.UnitX,
            });
            entities.EventBus.RaiseLocalEvent(shell, ref collision);
            Assert.That(explosive.Exploded, Is.EqualTo(!shieldContact),
                "Shield contacts must absorb explosions; ordinary hull contacts must retain explosive damage.");
            if (shieldContact)
            {
                Assert.That(projectile.ProjectileSpent, Is.True);
                Assert.That(emitter.Damage, Is.EqualTo(expectedDamage).Within(0.01f), "Direct and explosive damage must charge exactly once.");
            }
            else
                Assert.That(emitter.Damage, Is.Zero);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
