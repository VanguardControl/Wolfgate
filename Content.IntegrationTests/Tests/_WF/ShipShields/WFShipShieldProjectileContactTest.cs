using System.Linq;
using Content.Server.Projectiles;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared.Projectiles;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Checks collision candidates do not charge shields before an actual projectile hit.</summary>
[TestFixture]
public sealed class WFShipShieldProjectileContactTest
{
    [Test]
    public async Task BroadphaseChecksDoNotDamageShieldButProjectileHitsDo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            var emitterUid = entities.SpawnEntity(null, map.GridCoords);
            var emitter = entities.EnsureComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ShipShieldComponent>(shield).Source = emitterUid;
            var projectileUid = entities.SpawnEntity("BulletDebugZoom", new EntityCoordinates(map.Grid.Owner, -3f, 0.5f));
            entities.EnsureComponent<ShipWeaponProjectileComponent>(projectileUid);
            var projectile = entities.GetComponent<ProjectileComponent>(projectileUid);
            var projectileBody = entities.GetComponent<PhysicsComponent>(projectileUid);
            var shieldBody = entities.GetComponent<PhysicsComponent>(shield);
            var shieldFixture = entities.GetComponent<FixturesComponent>(shield).Fixtures.Values.First();
            var projectileFixture = entities.GetComponent<FixturesComponent>(projectileUid).Fixtures.Values.First();
            var broadphase = new PreventCollideEvent(shield, projectileUid, shieldBody, projectileBody, shieldFixture, projectileFixture);
            entities.EventBus.RaiseLocalEvent(shield, ref broadphase);
            Assert.That(broadphase.Cancelled, Is.False);
            Assert.That(emitter.Damage, Is.Zero, "A broadphase candidate is not an impact.");
            Assert.That(projectile.ProjectileSpent, Is.False);

            entities.System<ProjectileSystem>().ProjectileCollide((projectileUid, projectile, projectileBody), shield);
            Assert.That(emitter.Damage, Is.EqualTo((float)projectile.Damage.GetTotal()));
            Assert.That(projectile.ProjectileSpent, Is.True);
            entities.System<ProjectileSystem>().ProjectileCollide((projectileUid, projectile, projectileBody), shield);
            Assert.That(emitter.Damage, Is.EqualTo((float)projectile.Damage.GetTotal()), "A spent shot cannot charge twice.");
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
