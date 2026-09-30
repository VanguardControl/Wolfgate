using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared._Mono.Weapons.Ranged.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Exercises physical and fast projectile impacts against a rotated hull shield.</summary>
[TestFixture]
public sealed class WFShipShieldMovingProjectileTest
{
    [TestCase(10f, false)]
    [TestCase(300f, false)]
    [TestCase(300f, true)]
    public async Task MovingShipProjectileRespectsShieldAndSourceGrid(float speed, bool outbound)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var transform = entities.System<SharedTransformSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var projectileUid = EntityUid.Invalid;
        var emitterUid = EntityUid.Invalid;
        var projectile = default(ProjectileComponent);
        var expectedDamage = 0f;
        var start = Vector2.Zero;
        var shieldUid = EntityUid.Invalid;
        var simulationStart = TimeSpan.Zero;

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 60);
            Assert.That(server.Timing.TickPeriod.TotalSeconds, Is.EqualTo(1d / 60d).Within(0.000001d));
            transform.SetWorldRotation(map.Grid.Owner, Angle.FromDegrees(37));
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            shieldUid = shield;
            simulationStart = server.Timing.CurTime;
            emitterUid = entities.SpawnEntity(null, map.GridCoords);
            entities.EnsureComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ShipShieldComponent>(shield).Source = emitterUid;
            var localStart = new EntityCoordinates(map.Grid.Owner, outbound ? 0.5f : -8f, 0.5f);
            var mapStart = transform.ToMapCoordinates(localStart);
            start = mapStart.Position;
            var mapAhead = transform.ToMapCoordinates(localStart.Offset(new Vector2(1f, 0f)));
            var direction = Vector2.Normalize(mapAhead.Position - start);
            projectileUid = entities.SpawnEntity("BulletDebugZoom", mapStart);
            entities.EnsureComponent<ShipWeaponProjectileComponent>(projectileUid);
            if (outbound)
                entities.EnsureComponent<ProjectileGridPhaseComponent>(projectileUid).SourceGrid = map.Grid.Owner;
            projectile = entities.GetComponent<ProjectileComponent>(projectileUid);
            expectedDamage = (float)projectile.Damage.GetTotal();
            physics.SetLinearVelocity(projectileUid, direction * speed);
            physics.WakeBody(projectileUid);
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            if (outbound)
            {
                Assert.That(entities.EntityExists(projectileUid), Is.True, "The originating ship must allow its own shot through.");
                Assert.That(projectile.ProjectileSpent, Is.False);
                Assert.That(emitter.Damage, Is.Zero);
                Assert.That(Vector2.Distance(transform.GetWorldPosition(projectileUid), start), Is.GreaterThan(20f));
            }
            else
            {
                var exists = entities.EntityExists(projectileUid);
                var body = exists ? entities.GetComponent<PhysicsComponent>(projectileUid) : null;
                var position = exists ? transform.GetWorldPosition(projectileUid) : Vector2.Zero;
                var local = exists ? transform.ToCoordinates(map.Grid.Owner, transform.GetMapCoordinates(projectileUid)).Position : Vector2.Zero;
                var shieldBody = entities.GetComponent<PhysicsComponent>(shieldUid);
                Assert.That(projectile.ProjectileSpent, Is.True,
                    $"A moving shot must contact the shield edge. Exists={exists}, world={position}, hullLocal={local}, start={start}, " +
                    $"distance={Vector2.Distance(position, start)}, velocity={body?.LinearVelocity}, mapVelocity={(exists ? physics.GetMapLinearVelocity(projectileUid) : Vector2.Zero)}, " +
                    $"canCollide={body?.CanCollide}, awake={body?.Awake}, shieldCollide={shieldBody.CanCollide}, shieldAwake={shieldBody.Awake}, " +
                    $"emitterDamage={emitter.Damage}, elapsed={(server.Timing.CurTime - simulationStart).TotalSeconds}, tickPeriod={server.Timing.TickPeriod.TotalSeconds}.");
                Assert.That(entities.EntityExists(projectileUid), Is.False);
                Assert.That(emitter.Damage, Is.EqualTo(expectedDamage), "Exactly one impact should charge the emitter.");
            }
        });
        await pair.CleanReturnAsync();
    }
}
