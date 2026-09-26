#nullable enable
using System.Linq;
using Content.Server._WF.Planets.Flight;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>An orbital faller who drops through a mouth is maimed on the cavern floor like on the ground, not killed.</summary>
[TestFixture]
[TestOf(typeof(WFOrbitalMobFallSystem))]
public sealed class CavernOrbitalFallTest
{
    /// <summary>Dropped from orbit over the gate: critical, one arm and one leg gone, on the cavern floor.</summary>
    [Test]
    public async Task OrbitalFallIntoMouthMaimsNotKills()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);

        // Fervidus lands on ash (x0.75), where the full orbital fall would kill; Asclepiu's water landing hurts nobody.
        var world = await BuildWorld(pair, "WFSurfaceFervidus");

        try
        {
            var gate = await Gate(pair, world);
            var orbit = world.Layers[^1];
            var mob = EntityUid.Invalid;
            var landed = false;

            // As OrbitalMobFallTest drops its faller: spawned beside the drop, then moved over it and pushed down.
            await server.WaitPost(() =>
            {
                mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(orbit, TileCentre(gate.Origin - new Vector2i(4, 4))));
                entMan.RunMapInit(mob, entMan.GetComponent<MetaDataComponent>(mob));
                server.System<SharedTransformSystem>().SetCoordinates(mob, new EntityCoordinates(orbit, TileCentre(gate.Origin)));
                server.System<CESharedZLevelsSystem>().SetZVelocity(mob, -2);
            });

            for (var i = 0; i < 200 && !landed; i++)
            {
                await pair.RunTicksSync(2);
                await server.WaitPost(() =>
                    landed = entMan.GetComponent<TransformComponent>(mob).MapUid == world.Cavern
                             && entMan.GetComponent<MobStateComponent>(mob).CurrentState != MobState.Alive);
            }

            await pair.RunTicksSync(2);

            await server.WaitAssertion(() =>
            {
                var body = server.System<SharedBodySystem>();
                var map = entMan.GetComponent<TransformComponent>(mob).MapUid;
                var z = entMan.GetComponent<CEZPhysicsComponent>(mob);

                Assert.That(landed, Is.True,
                    $"The orbital faller never came down hurt in the cavern: map {entMan.ToPrettyString(map)}, v={z.Velocity}, h={z.LocalPosition}, state {entMan.GetComponent<MobStateComponent>(mob).CurrentState}.");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(map, Is.EqualTo(world.Cavern), "The orbital faller is not in the cavern.");
                    Assert.That(entMan.GetComponent<MobStateComponent>(mob).CurrentState, Is.Not.EqualTo(MobState.Dead),
                        "A fall from orbit through a mouth killed the faller.");
                    Assert.That(entMan.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(MobState.Critical),
                        "A fall from orbit through a mouth should leave the faller critical.");
                    Assert.That(body.GetBodyChildrenOfType(mob, BodyPartType.Arm).Count(), Is.EqualTo(1),
                        "The orbital faller should have lost exactly one arm.");
                    Assert.That(body.GetBodyChildrenOfType(mob, BodyPartType.Leg).Count(), Is.EqualTo(1),
                        "The orbital faller should have lost exactly one leg.");
                    Assert.That(entMan.HasComponent<WFOrbitalMobFallComponent>(mob), Is.False,
                        "The orbital fall marker outlived the impact.");
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }
}
