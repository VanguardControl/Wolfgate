#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Client._WF.Planets;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Server._WF.Planets;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.Planets;
using Content.Shared.CCVar;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>What a listener hears underground: the surface's day and night bed, muffled, unless the cavern names its own.</summary>
[TestFixture]
[TestOf(typeof(WFCavernSystem))]
public sealed class CavernAmbienceTest
{
    private const string Surface = "WFSurfaceAsclepiu";
    private const string Beds = "/Audio/_WF/Planets/Worlds/";
    private const string DayBed = Beds + "asclepiu/asclepiu_ambience_day_loop_1.ogg";
    private const string NightBed = Beds + "asclepiu/asclepiu_ambience_night_loop_1.ogg";
    private const string Override = "WFPlanetAmbienceFervidus";

    /// <summary>
    /// A listener walking from the ground into the cavern keeps the same surface bed, now muffled and quieter; the
    /// cavern's clock follows the ground's, so night brings the muffled night bed; back on the ground the bed is clear.
    /// A cavern that names its own soundscape plays that instead, unmuffled.
    /// </summary>
    [Test]
    public async Task CavernHearsTheSurfaceMuffled()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var cavern = CavernOf(pair, Surface);

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, Surface);
        var cfg = client.ResolveDependency<IConfigurationManager>();
        var oldGain = 0f;

        try
        {
            await server.WaitPost(() =>
            {
                var biomes = server.System<BiomeSystem>();
                biomes.SetEnabled((world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground)), false);
                biomes.SetEnabled((world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern)), false);
            });

            await SetHour(pair, world, 12);
            var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, Vector2.Zero);
            await server.WaitPost(() => entMan.RemoveComponent<CEZPhysicsComponent>(viewer));
            await client.WaitPost(() =>
            {
                oldGain = cfg.GetCVar(CCVars.AmbienceVolume);
                cfg.SetCVar(CCVars.AmbienceVolume, 1f);
            });

            // The bed fades in from silence at 12 dB a second.
            await pair.RunTicksSync(pair.SecondsToTicks(7));
            var (surfaceBed, surfaceVolume) = await SingleBed(pair, DayBed, 0f);

            await MoveTo(pair, viewer, world.Cavern);
            await pair.RunTicksSync(pair.SecondsToTicks(4));

            await server.WaitAssertion(() =>
            {
                var ambience = entMan.GetComponent<WFPlanetAmbienceComponent>(world.Cavern);
                var environment = entMan.GetComponent<WFPlanetEnvironmentComponent>(world.Cavern);
                var above = entMan.GetComponent<WFPlanetEnvironmentComponent>(world.Ground);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(ambience.Profile, Is.EqualTo(entMan.GetComponent<WFPlanetAmbienceComponent>(world.Ground).Profile),
                        "The cavern does not take the surface's soundscape.");
                    Assert.That(ambience.Occlusion, Is.EqualTo(cavern.SurfaceAmbienceOcclusion), "The cavern's soundscape is not muffled.");
                    Assert.That(environment.MinuteOfDay, Is.EqualTo(above.MinuteOfDay).Within(1), "The cavern's clock does not follow the ground's.");
                    Assert.That(environment.PlanetName, Is.EqualTo(above.PlanetName), "The cavern reports another planet.");
                }
            });

            var (cavernBed, cavernVolume) = await SingleBed(pair, DayBed, cavern.SurfaceAmbienceOcclusion);
            Assert.That(cavernBed, Is.EqualTo(surfaceBed), "Going underground restarted the bed instead of muffling it.");
            Assert.That(cavernVolume, Is.LessThan(surfaceVolume - 3f), $"The bed is no quieter underground: {cavernVolume} dB against {surfaceVolume} dB.");

            await SetHour(pair, world, 0.5);
            await pair.RunTicksSync(pair.SecondsToTicks(10));
            await SingleBed(pair, NightBed, cavern.SurfaceAmbienceOcclusion);

            await MoveTo(pair, viewer, world.Ground);
            await pair.RunTicksSync(pair.SecondsToTicks(3));
            await SingleBed(pair, NightBed, 0f);

            // A cavern with its own soundscape plays it clear, crossfading out of the surface bed.
            cavern.Ambience = Override;
            await MoveTo(pair, viewer, world.Cavern);
            await pair.RunTicksSync(pair.SecondsToTicks(8));
            await SingleBed(pair, Beds + "fervidus/fervidus_ambience_loop_1.ogg", 0f);
        }
        finally
        {
            cavern.Ambience = null;
            await client.WaitPost(() => cfg.SetCVar(CCVars.AmbienceVolume, oldGain));
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Sets the world's clock to a local hour.</summary>
    private static async Task SetHour(TestPair pair, World world, double hour)
    {
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var state = server.EntMan.GetComponent<WFPlanetWeatherComponent>(world.Network);
            var weather = server.ResolveDependency<IPrototypeManager>().Index(state.Profile);
            state.Epoch = server.ResolveDependency<IGameTiming>().CurTime -
                          TimeSpan.FromSeconds((hour - weather.InitialHour) / 24 * weather.DaySeconds);
        });
    }

    private static async Task MoveTo(TestPair pair, EntityUid viewer, EntityUid map)
    {
        await pair.Server.WaitPost(() => pair.Server.System<SharedTransformSystem>().SetCoordinates(viewer, new EntityCoordinates(map, Vector2.Zero)));
    }

    /// <summary>Asserts the client plays exactly one planet bed, this one, at this occlusion; returns its stream and volume.</summary>
    private static async Task<(EntityUid Stream, float Volume)> SingleBed(TestPair pair, string file, float occlusion)
    {
        var client = pair.Client;
        var result = (EntityUid.Invalid, 0f);

        await client.WaitAssertion(() =>
        {
            var beds = new List<(EntityUid Uid, AudioComponent Audio, string File)>();
            var query = client.EntMan.EntityQueryEnumerator<AudioComponent>();
            while (query.MoveNext(out var uid, out var audio))
            {
                string name = audio.FileName;
                if (name.StartsWith(Beds) && name.Contains("_ambience_"))
                    beds.Add((uid, audio, name));
            }

            Assert.That(beds.Select(bed => bed.File), Is.EqualTo(new[] { file }), "The wrong beds are playing.");
            // A headless client shares one dummy source between streams, so the muffle is read from the player.
            Assert.That(client.System<WFPlanetAmbienceSystem>().Occlusion, Is.EqualTo(occlusion).Within(0.01f), $"{file} plays at the wrong occlusion.");
            result = (beds[0].Uid, beds[0].Audio.Params.Volume);
        });

        return result;
    }
}
