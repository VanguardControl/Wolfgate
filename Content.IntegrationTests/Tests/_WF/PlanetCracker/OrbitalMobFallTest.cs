using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetCracker.Flight;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Audio.Components;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

[TestFixture]
public sealed class OrbitalMobFallTest
{
    [Test]
    public async Task OrbitalFallSeversOneArmAndLegAndLeavesSurvivableCrit()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-8, -8), new Vector2i(16, 16));
        var em = pair.Server.EntMan;
        EntityUid mob = default;
        var landed = false;
        await pair.Server.WaitPost(() =>
        {
            mob = em.SpawnEntity("MobHuman", new EntityCoordinates(layers[^1], new Vector2(0.5f)));
            em.RunMapInit(mob, em.GetComponent<MetaDataComponent>(mob));
            pair.Server.System<SharedTransformSystem>().SetCoordinates(mob,
                new EntityCoordinates(layers[^1], new Vector2(3.5f)));
            pair.Server.System<CESharedZLevelsSystem>().SetZVelocity(mob, -2);
        });
        for (var i = 0; i < 150 && !landed; i++)
        {
            await pair.RunTicksSync(2);
            await pair.Server.WaitPost(() =>
                landed = em.GetComponent<TransformComponent>(mob).MapUid == layers[0]
                    && em.GetComponent<MobStateComponent>(mob).CurrentState != MobState.Alive);
        }
        await pair.Server.WaitAssertion(() =>
        {
            var z = em.GetComponent<CEZPhysicsComponent>(mob);
            Assert.That(landed, Is.True, $"map={em.GetComponent<TransformComponent>(mob).MapUid}, ground={layers[0]}, v={z.Velocity}, h={z.LocalPosition}, cached={z.CachedGroundHeight}, state={em.GetComponent<MobStateComponent>(mob).CurrentState}, marked={em.HasComponent<WFOrbitalMobFallComponent>(mob)}");
            Assert.That(em.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(MobState.Critical));
            var body = pair.Server.System<SharedBodySystem>();
            Assert.That(body.GetBodyChildrenOfType(mob, BodyPartType.Arm).Count(), Is.EqualTo(1));
            Assert.That(body.GetBodyChildrenOfType(mob, BodyPartType.Leg).Count(), Is.EqualTo(1));
            Assert.That(em.HasComponent<WFOrbitalMobFallComponent>(mob), Is.False);
            var audio = em.EntityQueryEnumerator<AudioComponent>();
            var splats = 0;
            while (audio.MoveNext(out _, out var clip))
                if (clip.FileName == "/Audio/Effects/gib1.ogg") splats++;
            Assert.That(splats, Is.EqualTo(1), "One orbital impact should play one grotesque splat.");
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(pair.Server.System<SharedBodySystem>().GetBodyChildrenOfType(mob, BodyPartType.Arm).Count(), Is.EqualTo(1));
            Assert.That(pair.Server.System<SharedBodySystem>().GetBodyChildrenOfType(mob, BodyPartType.Leg).Count(), Is.EqualTo(1));
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SurfaceImpactWithoutOrbitalFallDoesNotSeverLimbs()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-4, -4), new Vector2i(8, 8));
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var mob = em.SpawnEntity("MobHuman", new EntityCoordinates(layers[0], new Vector2(0.5f)));
            var hit = new CEZLevelHitEvent(1f);
            em.EventBus.RaiseLocalEvent(mob, ref hit);
            var body = pair.Server.System<SharedBodySystem>();
            Assert.That(body.GetBodyChildrenOfType(mob, BodyPartType.Arm).Count(), Is.EqualTo(2));
            Assert.That(body.GetBodyChildrenOfType(mob, BodyPartType.Leg).Count(), Is.EqualTo(2));
            Assert.That(em.HasComponent<WFOrbitalMobFallComponent>(mob), Is.False);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
