#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._CE.ZLevels.Core;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>The hull guard: with a cavern below, every hull path still stops at the ground.</summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class CavernHullTest
{
    /// <summary>A hull with no lift over unloaded ground churns on the ground and never sinks into the cavern.</summary>
    [Test]
    public async Task UnsupportedHullNeverDescends()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");

        // The 15x15 test hull, away from the planet centre. Its chunks are never loaded: a hull parked on loaded
        // terrain can keep a tile or two through an unload, because biome entities it touched pin theirs.
        var from = new Vector2i(200, 200);
        var to = new Vector2i(214, 214);

        var hull = await PlanetFixture.BuildHull(pair, await MapIdOf(pair, world.Ground), new Vector2(from.X, from.Y));
        await PlanetFixture.MapInitHull(pair, hull);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(world.Ground),
                "Precondition: the hull is not on the ground map."));
        await AssertNoGroundUnder(pair, world, from, to);

        await AssertNeverBelowGround(pair, world, hull, seconds: 10);

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>A pilot holding descend over unloaded ground goes nowhere below it.</summary>
    [Test]
    public async Task PilotCannotDescendFromGround()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var levels = server.System<CEZLevelsSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");

        // The 7x9 lander, hovering on its landing thrusters over ground whose chunks are not loaded.
        var from = new Vector2i(200, 200);
        var to = new Vector2i(206, 208);

        var lander = await PlanetFixture.BuildLander(pair, await MapIdOf(pair, world.Ground), new Vector2(from.X, from.Y));
        await PlanetFixture.MapInitHull(pair, lander);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<TransformComponent>(lander).MapUid, Is.EqualTo(world.Ground),
                "Precondition: the lander is not on the ground map."));
        await AssertNoGroundUnder(pair, world, from, to);

        await server.WaitAssertion(() =>
            Assert.That(levels.WfTryGetLiftRatio(lander, out var ratio) && ratio >= 1f, Is.True,
                "Precondition: the lander can't hover, so its pilot's descend key is never read."));

        await PlanetFixture.HoldDescend(pair, lander);

        // Its landing thrusters hold it up, so a refused descend leaves it where it is instead of churning.
        await AssertNeverBelowGround(pair, world, lander, seconds: 10, stayOn: world.Ground);

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>With a cavern below, a lander still lifts off into the first air layer and lands back on the ground.</summary>
    [Test]
    public async Task LiftoffAndLandingUnchanged()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var levels = server.System<CEZLevelsSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");
        var air = world.Layers[1];

        // A laid pad the loader never touches, so the hull lands on terrain rather than crashing through it.
        await PlanetFixture.LayTiles(pair, world.Ground, new Vector2i(-4, -4), new Vector2i(12, 14));

        var lander = await PlanetFixture.BuildLander(pair, await MapIdOf(pair, world.Ground), Vector2.Zero);
        await PlanetFixture.MapInitHull(pair, lander);
        var pilot = await PlanetFixture.HoldVertical(pair, lander, ShuttleButtons.None);
        var console = PlanetFixture.FindShuttleConsole(entMan, lander);

        await server.WaitAssertion(() =>
            Assert.That(levels.WfTryBeginLiftoff(lander, console, pilot, out var reason), Is.True,
                $"A grounded lander was refused liftoff with caverns on: {reason}"));

        var airborne = await WaitForMap(pair, world, lander, air, seconds: 45);
        Assert.That(airborne, Is.True, "The lander never lifted off into the first air layer with caverns on.");

        await server.WaitPost(() => entMan.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.DescendZ);

        var landed = await WaitForMap(pair, world, lander, world.Ground, seconds: 40);
        Assert.That(landed, Is.True, "The lander never landed back on the ground with caverns on.");

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>Fails unless every ground tile under the hull's footprint is empty.</summary>
    private static async Task AssertNoGroundUnder(TestPair pair, World world, Vector2i from, Vector2i to)
    {
        var server = pair.Server;
        var maps = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
            Assert.That(SolidTiles(server.EntMan, maps, world.Ground, from, to), Is.Zero,
                "Precondition: there is ground under the hull."));
    }

    /// <summary>Ticks one at a time, failing if the hull or any transit ever opens onto the cavern, or the hull leaves <paramref name="stayOn"/>.</summary>
    // Every tick, so a transit gap entered and left between samples is still caught.
    private static async Task AssertNeverBelowGround(TestPair pair, World world, EntityUid hull, int seconds, EntityUid? stayOn = null)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticks = pair.SecondsToTicks(seconds);
        EntityUid? last = null;
        var trail = "";

        for (var tick = 0; tick < ticks; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var map = entMan.GetComponent<TransformComponent>(hull).MapUid;

                if (map != last)
                    trail += $" t{tick}:{entMan.ToPrettyString(map)}";
                last = map;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(TouchesCavern(entMan, map, world.Cavern), Is.False,
                        $"The hull went below the ground into {entMan.ToPrettyString(map)}.{trail}");
                    Assert.That(TransitsTouchingCavern(entMan, world.Cavern), Is.Empty,
                        $"A transit gap opened onto the cavern.{trail}");

                    if (stayOn is { } expected)
                        Assert.That(map, Is.EqualTo(expected), $"The hull left {entMan.ToPrettyString(expected)}.{trail}");
                }
            });
        }
    }

    /// <summary>Ticks a second at a time until the hull is on the map, failing if it ever goes below ground.</summary>
    private static async Task<bool> WaitForMap(TestPair pair, World world, EntityUid hull, EntityUid target, int seconds)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var reached = false;

        for (var second = 0; second < seconds && !reached; second++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitAssertion(() =>
            {
                var map = entMan.GetComponent<TransformComponent>(hull).MapUid;

                Assert.That(TouchesCavern(entMan, map, world.Cavern), Is.False,
                    $"The hull went below the ground into {entMan.ToPrettyString(map)}.");
                reached = map == target;
            });
        }

        return reached;
    }
}
