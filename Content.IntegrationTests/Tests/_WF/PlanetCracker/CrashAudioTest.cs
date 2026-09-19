#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// How loud one hull crash is allowed to be. CEZLevelsSystem.CrashGrid queues an explosion per hull tile and every
/// explosion that spawns plays two networked audio streams, so a 15x15 test hull was worth over a hundred of them at
/// once - which is what exhausted the live client's OpenAL sources and killed it as a fallen ship hit the surface.
/// </summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class CrashAudioTest
{
    /// <summary>The whole point: a crash is one bang, not a hull's worth of them.</summary>
    private const int ExplosionBudget = 4;

    /// <summary>Every sound the whole crash is allowed to make, wreckage included.</summary>
    private const int TotalBudget = 30;

    /// <summary>The folder every explosion clip lives in; the client died on /Audio/Effects/explosion4.ogg.</summary>
    private const string ExplosionClips = "/Audio/Effects/explosion";

    /// <summary>
    /// A code-built hull dropped onto prepared terrain crashes, and the whole crash is worth a single-digit number of
    /// audio entities.
    /// The hull is started on the air layer directly above the ground rather than in orbit: the fall itself is the same
    /// plummet through the same transit machinery either way (CEZGridFallerComponent's gravity is 0.15 levels/s², so
    /// each extra layer is another four seconds of ticks), and what is being counted here is the landing.
    /// </summary>
    [Test]
    public async Task OneHullCrashIsOneBang()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var lowAir = layers[1];
        var lowAirMapId = await MapIdOf(pair, lowAir);

        // A biome only generates under a viewer, and the crash test needs solid tiles under the footprint for the
        // landing to count as a crash at all (CEZLevelsSystem.Gravity.cs HasGroundUnderFootprint).
        await LayTiles(pair, ground, new Vector2i(-4, -4), new Vector2i(20, 20));

        var hull = await BuildCracker(pair, lowAirMapId);
        await MapInitHull(pair, hull);

        var seen = new HashSet<EntityUid>();
        var clips = new Dictionary<string, int>();
        var crashed = false;
        var peakAudio = 0;

        // The fall gate holds a fresh grid for its three second grace, then plummets it; a full gap at 0.15 levels/s²
        // is another three and a half. Sampling every other tick catches every audio entity: the shortest explosion
        // clip outlives two ticks by an order of magnitude.
        for (var i = 0; i < 450; i++)
        {
            await server.WaitRunTicks(2);
            await server.WaitPost(() =>
            {
                var alive = 0;
                var query = entMan.EntityQueryEnumerator<AudioComponent>();
                while (query.MoveNext(out var uid, out var audio))
                {
                    alive++;
                    if (seen.Add(uid))
                        clips[audio.FileName] = clips.GetValueOrDefault(audio.FileName) + 1;
                }

                peakAudio = Math.Max(peakAudio, alive);
                if (entMan.GetComponent<TransformComponent>(hull).MapUid == ground)
                    crashed = true;
            });
        }

        var bangs = 0;
        foreach (var (clip, count) in clips)
        {
            if (clip.StartsWith(ExplosionClips))
                bangs += count;
        }

        var breakdown = string.Join(", ", clips.Select(clip => $"{clip.Key} x{clip.Value}"));

        TestContext.Out.WriteLine($"Crash audio: {bangs} explosion clips, {seen.Count} audio entities. {breakdown}");

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(crashed, Is.True,
                    "The hull never reached the ground layer, so no crash was measured at all.");
                // A free fall arrives above the hard-landing threshold, so this really is the crash path; without the
                // bang there would be nothing here to keep a budget on (FlightTest.FreeFallCrashes...).
                Assert.That(clips.Where(c => c.Key.StartsWith("/Audio/_WF/PlanetCracker/Flight/crash")).Sum(c => c.Value), Is.EqualTo(1),
                    "Structural breakup must play one impact sound, not a ship-wide explosion.");
                Assert.That(bangs, Is.LessThanOrEqualTo(ExplosionBudget),
                    $"One hull crash played {bangs} explosion clips. Every hull tile queues its own crater and each " +
                    $"blast that sounds costs two OpenAL sources on every client that can hear it, which is what killed " +
                    $"the live client. Everything heard: {breakdown}");
                // Concurrent sources exhaust the client; successive ambient loops do not.
                Assert.That(peakAudio, Is.LessThanOrEqualTo(TotalBudget),
                    $"One hull crash peaked at {peakAudio} concurrent audio entities ({seen.Count} over the whole test). Everything heard: {breakdown}");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The map id of a z-layer, for the spawners that want one.</summary>
    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapId = MapId.Nullspace;

        await server.WaitPost(() => mapId = entMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }
}
