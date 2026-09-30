using System.Numerics;
using Content.Server._WF.ShipShields;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio.Components;
using Content.Shared.Explosion.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Shared.Projectiles;
using Content.Shared._Mono.Weapons.Ranged.Components;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Checks damaged shields keep intercepting real artillery after hull fixture rebuilds.</summary>
[TestFixture]
public sealed class WFShipShieldHullDamageTest
{
    [Test]
    public async Task DamagedHullRebuildKeepsShieldCollidableForArtillery()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        await pair.Client.WaitAssertion(() =>
        {
            var resources = pair.Client.ResolveDependency<IResourceCache>();
            for (var variant = 1; variant <= 3; variant++)
            {
                var clip = resources.GetResource<AudioResource>($"/Audio/_WF/ShipShields/impact_{variant}.ogg").AudioStream;
                Assert.That(clip.ChannelCount, Is.EqualTo(1), "Positional shield impacts must load as mono audio.");
                Assert.That(clip.Length.TotalSeconds, Is.EqualTo(3.4d).Within(0.03d));
            }
        });
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();
        var transform = entities.System<SharedTransformSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var shield = EntityUid.Invalid;
        var emitterUid = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 60);
            maps.SetTile(map.Grid, new Vector2i(1, 0), map.Tile.Tile);
            transform.SetWorldRotation(map.Grid.Owner, Angle.FromDegrees(37));
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            emitterUid = entities.SpawnEntity(null, map.GridCoords);
            var emitter = entities.EnsureComponent<ShipShieldEmitterComponent>(emitterUid);
            emitter.Damage = 2500f;
            entities.GetComponent<ShipShieldComponent>(shield).Source = emitterUid;
            maps.SetTile(map.Grid, Vector2i.Zero, default);
            maps.SetTile(map.Grid, Vector2i.Zero, map.Tile.Tile);
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<PhysicsComponent>(shield).CanCollide, Is.True,
                "Destroying the last old fixture must not disable the rebuilt shield.");
            Assert.That(entities.GetComponent<WFShipShieldVisualsComponent>(shield).Health, Is.InRange(0.1f, 0.45f));
        });

        var shots = new[]
        {
            (Prototype: "20mmBullet", Start: new Vector2(-12f, 0.5f), Speed: 50f, Tickrate: 60),
            (Prototype: "ShipCerberusPlasma", Start: new Vector2(-12f, -12f), Speed: 50f, Tickrate: 60),
            (Prototype: "20mmBullet", Start: new Vector2(14f, 0.5f), Speed: 50f, Tickrate: 60),
            (Prototype: "ShipCerberusPlasma", Start: new Vector2(-12f, -10f), Speed: 74.9f, Tickrate: 60),
            (Prototype: "ShipCerberusPlasma", Start: new Vector2(-10f, -12f), Speed: 74.9f, Tickrate: 30),
        };
        var nextImpactSound = TimeSpan.Zero;
        var shotIndex = 0;
        foreach (var shot in shots)
        {
            var uid = EntityUid.Invalid;
            var projectile = default(ProjectileComponent);
            var expectedDamage = 0f;
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, shot.Tickrate);
                var start = transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, shot.Start));
                var target = transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, 1f, 0.5f));
                var direction = Vector2.Normalize(target.Position - start.Position);
                uid = entities.SpawnEntity(shot.Prototype, start);
                transform.SetWorldRotation(uid, direction.ToAngle());
                projectile = entities.GetComponent<ProjectileComponent>(uid);
                expectedDamage = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage + (float)projectile.Damage.GetTotal();
                if (entities.TryGetComponent<ExplosiveComponent>(uid, out var explosive) &&
                    prototypes.TryIndex(explosive.ExplosionType, out var explosion))
                    expectedDamage += explosive.TotalIntensity * (float)explosion.DamagePerIntensity.GetTotal();
                physics.SetLinearVelocity(uid, direction * shot.Speed);
                physics.WakeBody(uid);
            });
            await pair.RunTicksSync(shot.Tickrate / 2);
            await server.WaitAssertion(() =>
            {
                var exists = entities.EntityExists(uid);
                var body = exists ? entities.GetComponent<PhysicsComponent>(uid) : null;
                var position = exists ? transform.ToCoordinates(map.Grid.Owner, transform.GetMapCoordinates(uid)).Position : Vector2.Zero;
                var phase = exists && entities.TryGetComponent<ProjectileGridPhaseComponent>(uid, out var source) ? source.SourceGrid : null;
                var actualDamage = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage;
                Assert.That(projectile.ProjectileSpent, Is.True,
                    $"{shot.Prototype} must hit the rebuilt shield from {shot.Start}. Exists={exists}, queued={(exists && entities.IsQueuedForDeletion(uid))}, " +
                    $"hullLocal={position}, velocity={body?.LinearVelocity}, collidable={body?.CanCollide}, sourceGrid={phase}, " +
                    $"actualEmitterDamage={actualDamage}, expectedEmitterDamage={expectedDamage}, shieldCollidable={entities.GetComponent<PhysicsComponent>(shield).CanCollide}.");
                Assert.That(entities.EntityExists(uid), Is.False);
                Assert.That(entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage, Is.EqualTo(expectedDamage).Within(0.01f));
                if (shotIndex >= 3)
                    return;
                var audio = entities.GetComponent<WFShipShieldImpactAudioComponent>(shield);
                if (nextImpactSound == TimeSpan.Zero)
                {
                    nextImpactSound = audio.NextImpactSound;
                    Assert.That((nextImpactSound - server.Timing.CurTime).TotalSeconds, Is.InRange(1.9d, 3.6d));
                }
                else
                    Assert.That(audio.NextImpactSound, Is.EqualTo(nextImpactSound), "Rapid contacts must retain the first impact's sound cooldown.");
                var soundCount = 0;
                var sounds = entities.EntityQueryEnumerator<AudioComponent>();
                while (sounds.MoveNext(out var soundUid, out var sound))
                {
                    var filename = sound.FileName;
                    if (!entities.IsQueuedForDeletion(soundUid) && filename.Contains("_WF/ShipShields/impact_"))
                        soundCount++;
                }
                Assert.That(soundCount, Is.EqualTo(1), "Consecutive contacts inside the cooldown must not start overlapping impact sounds.");
            });
            shotIndex++;
        }
        await pair.CleanReturnAsync();
    }
}
