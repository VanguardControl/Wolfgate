#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// The orbit layer holds a hull up no matter how it got there. The fall gate's own exemption
/// (CEZLevelsSystem.Gravity.cs:93) only covers the plummet path; the generic z-physics integrator in
/// CESharedZLevelsSystem.Update.cs:113 walks a woken grid down one map per level with a direct parent change
/// (CESharedZLevelsSystem.Movement.cs:452 -> CEZLevelsSystem.Transit.cs:101), skipping transit and the crash entirely.
/// These cover both arrival paths and the only way down that is supposed to work.
/// </summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class OrbitArrivalTest
{
    /// <summary>Real FTL travel time for the arrival test; long enough to pass through the FTL map, short enough to tick.</summary>
    private const float FtlStartup = 0.2f;

    private const float FtlTravel = 0.5f;

    /// <summary>
    /// The live defect: a hull that arrives in orbit through the real FTL travel path, with no gravgen at all, is still
    /// on the orbit layer ten seconds later. Before the fix it walked straight down to the ground map through direct
    /// parent changes, one per layer, with no transit map and no crash.
    /// </summary>
    [Test]
    public async Task FtlArrivalStaysInOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];

        var origin = await pair.CreateTestMap();
        var hull = await BuildCracker(pair, origin.MapId);
        await OpenOriginTile(pair, hull);
        await MapInitHull(pair, hull);

        await FtlTo(pair, hull, orbit);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                "Precondition: the FTL did not put the hull on the orbit layer at all.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10f));

        await server.WaitAssertion(() =>
        {
            var mapUid = entMan.GetComponent<TransformComponent>(hull).MapUid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mapUid, Is.EqualTo(orbit),
                    $"The hull left the orbit layer for {entMan.ToPrettyString(mapUid)} without being told to descend.");
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(mapUid), Is.False,
                    "The hull started falling out of orbit on its own.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The same hull placed straight onto the orbit layer with no gravgen, woken by its own map-init, stays put. This is
    /// the spawn path rather than the arrival path, and it breaks the same way: the z-physics integrator needs no FTL to
    /// start walking a grid down.
    /// </summary>
    [Test]
    public async Task SpawnedHullStaysInOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var zLevels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMapId = await MapIdOf(pair, orbit);

        var hull = await BuildCracker(pair, orbitMapId);
        await OpenOriginTile(pair, hull);
        await MapInitHull(pair, hull);
        await Nudge(pair, hull);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "Precondition: the hull did not start on the orbit layer.");
                Assert.That(entMan.HasComponent<CEZGridFallerComponent>(hull), Is.True,
                    "Precondition: the hull is not on a z-network at all, so nothing is being tested.");
                Assert.That(entMan.GetComponent<CEZPhysicsComponent>(hull).CachedGroundHeight, Is.LessThan(0f),
                    "Precondition: the hull still reads solid ground under itself, so the sinking path is not armed " +
                    "and this would pass without the orbit hold.");
                Assert.That(zLevels.WfIsParkedInOrbit((hull, entMan.GetComponent<MapGridComponent>(hull))), Is.True,
                    "The orbit hold is not engaged on the hull.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10f));

        await server.WaitAssertion(() =>
        {
            var mapUid = entMan.GetComponent<TransformComponent>(hull).MapUid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mapUid, Is.EqualTo(orbit),
                    $"The hull sank off the orbit layer to {entMan.ToPrettyString(mapUid)} with no descend input.");
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(mapUid), Is.False,
                    "The hull started falling out of orbit on its own.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The deliberate way down: the console's enter-atmosphere action puts the hull into the gap between orbit and the
    /// layer below, never straight onto a lower layer and never onto the ground. A hull with no lift still gets to
    /// fall - otherwise a cold ship parked in orbit by the fix above would be stranded there for the rest of the
    /// round - it just has to confirm first (F10; the raw descend input out of orbit is refused, see FlightTest).
    /// </summary>
    [Test]
    public async Task DescendFromOrbitEntersTransit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var ground = layers[0];
        var topAir = layers[^2];
        var orbitMapId = await MapIdOf(pair, orbit);

        var hull = await BuildCracker(pair, orbitMapId);
        await OpenOriginTile(pair, hull);
        await MapInitHull(pair, hull);

        var refusal = await EnterAtmosphere(pair, hull);

        Assert.That(refusal, Is.Null, $"The confirmed descent was refused: {refusal}");

        await server.WaitAssertion(() =>
        {
            var mapUid = entMan.GetComponent<TransformComponent>(hull).MapUid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mapUid, Is.Not.EqualTo(ground),
                    "The descend dropped the hull straight onto the ground map, skipping every layer between.");
                Assert.That(mapUid, Is.Not.EqualTo(topAir),
                    "The descend hopped the hull directly onto the air layer instead of flying it down through the gap.");
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(mapUid), Is.True,
                    $"The descending hull is on {entMan.ToPrettyString(mapUid)}, which is not a transit map.");
            }
        });

        await server.WaitAssertion(() =>
        {
            var transit = entMan.GetComponent<CEZTransitMapComponent>(
                entMan.GetComponent<TransformComponent>(hull).MapUid!.Value);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(transit.UpperMap, Is.EqualTo(orbit),
                    "The hull is in a gap that does not hang off the orbit layer, so it skipped a level on the way in.");
                Assert.That(transit.LowerMap, Is.EqualTo(topAir),
                    "The hull is in a gap whose floor is not the layer directly below orbit.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Clears the hull tile the grid origin sits on. Orbit is bare vacuum, so the shared ground-height walk finds a
    /// floor under a parked hull only when the hull's own origin happens to land on one of its own tiles - which is
    /// where the code-built test hull starts and where a mapped ship usually does not. With the origin over open space
    /// the walk reports -1, which is the state the whole defect lives in: every hull that reads it sinks a level per
    /// crossing. Without this the test passes on unfixed code, for the wrong reason.
    /// </summary>
    private static async Task OpenOriginTile(TestPair pair, EntityUid hull)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(hull);
            maps.SetTiles(hull, grid, new List<(Vector2i, Tile)> { (Vector2i.Zero, Tile.Empty) });
        });

        await server.WaitRunTicks(1);
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

    /// <summary>
    /// Flies a hull to a layer through the real FTL machinery, so the arrival carries everything a live jump does: the
    /// stop on the FTL map, the parent changes, and the velocity an arriving hull still has.
    /// </summary>
    private static async Task FtlTo(TestPair pair, EntityUid hull, EntityUid layer)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var shuttles = server.System<ShuttleSystem>();

        await server.WaitPost(() =>
        {
            var shuttle = entMan.GetComponent<ShuttleComponent>(hull);
            shuttles.FTLToCoordinates(hull,
                shuttle,
                new EntityCoordinates(layer, Vector2.Zero),
                Angle.Zero,
                FtlStartup,
                FtlTravel);
        });

        // The arrival phase runs on the server's own ftl.arrival_time, which no argument here overrides, so the jump is
        // waited out rather than timed.
        var arrived = false;
        for (var i = 0; i < 60 && !arrived; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
            await server.WaitPost(() =>
                arrived = entMan.GetComponent<TransformComponent>(hull).MapUid == layer);
        }
    }

    /// <summary>
    /// Shoves a hull a hair so the z-physics body is awake. A grid that never moves is never added to the active body
    /// list (CESharedZLevelsSystem.Activation.cs:138), which would make the spawn case pass for the wrong reason.
    /// </summary>
    private static async Task Nudge(TestPair pair, EntityUid hull)
    {
        var server = pair.Server;
        var transform = server.System<SharedTransformSystem>();

        await server.WaitPost(() => transform.SetLocalPosition(hull, new Vector2(0.5f, 0.5f)));
        await server.WaitRunTicks(1);
    }
}
