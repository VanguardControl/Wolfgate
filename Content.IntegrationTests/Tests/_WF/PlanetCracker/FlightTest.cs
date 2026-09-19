#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Content.Server.Decals;
using Content.Server.Parallax;
using Content.Shared.Parallax.Biomes;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.PlanetCracker.Flight;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Flight;
using Content.Shared._WF.ShipPa;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Content.Shared.Movement.Systems;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// F10, atmospheric flight: what lifts a hull over a planet, what happens when it stops lifting, and what it costs to
/// hit the ground. Everything here runs on the standalone Asclepiu stack, which is the only place a planet layer and
/// its gravity exist at all.
/// </summary>
[TestFixture]
[TestOf(typeof(WFFlightSystem))]
public sealed class FlightTest
{
    /// <summary>The conversion kit item.</summary>
    private const string Kit = "WFLandingThrusterKit";

    /// <summary>The stock thruster the kit converts.</summary>
    private const string PlainThruster = "DebugThruster";

    /// <summary>The wind loop, as the audio entity reports it.</summary>
    private const string WindClip = "/Audio/_WF/PlanetCracker/Flight/atmo_wind.ogg";

    /// <summary>The fall rumble, as the audio entity reports it.</summary>
    private const string RumbleClip = "/Audio/_WF/PlanetCracker/Flight/fall_rumble.ogg";

    /// <summary>The tiny cracker's hull mass: 225 tiles at TileDensityMultiplier 0.5.</summary>
    private const float CrackerHullMass = 112.5f;

    /// <summary>WFAnchorCrateComponent.VirtualMass, times the cracker's two crates.</summary>
    private const float CrackerCargoMass = 12f;

    /// <summary>
    /// Every sound two hulls are allowed to make grinding out a hard landing over three seconds. As built it is five:
    /// a thud and a scrape loop each, and the atmosphere's own wind. The floor under the number is that none of the
    /// skid's damage - the crush, the plough, the craters - is allowed to be a sound per victim per tick.
    /// </summary>
    private const int AudioBudget = 24;

    /// <summary>
    /// A gravity generator lifts nothing over a planet and landing thrusters lift everything. The cracker's own
    /// centrifuge is rated at 3000 against a 124.5 load, so without the exclusion the hull would read as flying.
    /// </summary>
    [Test]
    public async Task SkidScarsStayBehindAndDoNotStackOrFillHolesAndRivers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(64, 64));
        var hull = await BuildCracker(pair, await MapIdOf(pair, ground));
        await MapInitHull(pair, hull);
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            var groundGrid = em.GetComponent<MapGridComponent>(ground);
            var grid = em.GetComponent<MapGridComponent>(hull);
            var flight = server.System<WFFlightSystem>();
            var skid = em.EnsureComponent<WFSkidComponent>(hull);
            var hole = new Vector2i(2, 2);
            var river = new Vector2i(3, 3);
            map.SetTile(ground, groundGrid, hole, Tile.Empty);
            em.SpawnEntity("WFBloodRiver", new EntityCoordinates(ground, new Vector2(3.5f, 3.5f)));
            var riverTile = map.GetTileRef(ground, groundGrid, river).Tile;
            var untouched = map.GetTileRef(ground, groundGrid, new Vector2i(45, 45)).Tile;
            flight.ScarSkidGround((hull, grid), skid);
            var scar = map.GetTileRef(ground, groundGrid, new Vector2i(7, 7)).Tile;
            Assert.That(scar.TypeId, Is.EqualTo(server.ResolveDependency<ITileDefinitionManager>()["FloorPlanetDirt"].TileId));
            Assert.That(map.GetTileRef(ground, groundGrid, hole).Tile.IsEmpty, Is.True);
            Assert.That(map.GetTileRef(ground, groundGrid, river).Tile, Is.EqualTo(riverTile));
            var decals = server.System<DecalSystem>();
            var bounds = new Box2(-8, -8, 64, 64);
            var count = decals.GetDecalsIntersecting(ground, bounds).Count();
            Assert.That(count, Is.GreaterThan(0));
            flight.ScarSkidGround((hull, grid), skid);
            Assert.That(decals.GetDecalsIntersecting(ground, bounds).Count(), Is.EqualTo(count));
            server.System<SharedTransformSystem>().SetWorldPosition(hull, new Vector2(20f, 0f));
            flight.ScarSkidGround((hull, grid), skid);
            Assert.That(map.GetTileRef(ground, groundGrid, new Vector2i(7, 7)).Tile, Is.EqualTo(scar));
            Assert.That(map.GetTileRef(ground, groundGrid, new Vector2i(27, 7)).Tile, Is.EqualTo(scar));
            Assert.That(map.GetTileRef(ground, groundGrid, new Vector2i(45, 45)).Tile, Is.EqualTo(untouched));
            var biome = em.GetComponent<BiomeComponent>(ground);
            Assert.That(server.System<BiomeSystem>().WfIsPinned((ground, biome), new Vector2i(7, 7)), Is.True);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SlowCrashKeepsMomentumAndStartsSkidding()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var hull = await BuildCracker(pair, await MapIdOf(pair, layers[0]));
        await MapInitHull(pair, hull);
        await server.WaitAssertion(() =>
        {
            em.EnsureComponent<WFLiftLostComponent>(hull);
            server.System<SharedPhysicsSystem>().SetLinearVelocity(hull, new Vector2(2f, 0f));
            var levels = server.System<CEZLevelsSystem>();
            levels.WfSkidAfterCrash(hull);
            Assert.That(em.HasComponent<WFSkidComponent>(hull), Is.True);
            Assert.That(em.GetComponent<PhysicsComponent>(hull).LinearVelocity.X, Is.EqualTo(2f));
            em.RemoveComponent<WFSkidComponent>(hull);
            Assert.That(levels.WfCrashSkidFriction(hull), Is.EqualTo(1f), "Ordinary landed ships retain ground grip.");
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [TestCase(0)]
    [TestCase(45)]
    public async Task LandingClearanceRemovesNearbyWallsButPreservesDistantAndOnboardWalls(int degrees)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-32, -32), new Vector2i(64, 64));
        var hull = await BuildCracker(pair, await MapIdOf(pair, ground));
        await MapInitHull(pair, hull);
        var near = EntityUid.Invalid;
        var far = EntityUid.Invalid;
        var aboard = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            var transform = server.System<SharedTransformSystem>();
            transform.SetLocalRotation(hull, Angle.FromDegrees(degrees));
            var matrix = transform.GetWorldMatrix(hull);
            near = em.SpawnEntity("WallSolid", new EntityCoordinates(ground,
                Vector2.Transform(new Vector2(-0.5f, 7.5f), matrix)));
            far = em.SpawnEntity("WallSolid", new EntityCoordinates(ground,
                Vector2.Transform(new Vector2(-4.5f, 7.5f), matrix)));
            aboard = em.SpawnEntity("WallSolid", new EntityCoordinates(hull, new Vector2(4.5f, 7.5f)));
            server.System<CEZLevelsSystem>().WfClearLandingObstacles(hull);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(near), Is.False, "The wreck needs a clear margin outside its deck.");
            Assert.That(em.EntityExists(far), Is.True, "Clearance must not flatten distant terrain.");
            Assert.That(em.EntityExists(aboard), Is.True, "Clearance must not delete the ship's own walls.");
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThrustersLiftAndGravgensDoNotOnAPlanet()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var zLevels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var topAir = layers[^2];
        var airMapId = await MapIdOf(pair, topAir);

        var hull = await BuildCracker(pair, airMapId);
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        await Energise(pair, hull);

        await server.WaitAssertion(() =>
        {
            Assert.That(zLevels.WfTryGetLiftRatio(hull, out var ratio), Is.True,
                "A hull on a planet air layer reports no lift ratio at all.");
            Assert.That(ratio, Is.EqualTo(0f).Within(0.001f),
                "The powered centrifuge is still counting as lift on a planet layer.");
        });

        await AddLandingThrusters(pair, hull, 2);

        await server.WaitAssertion(() =>
        {
            zLevels.WfTryGetLiftRatio(hull, out var ratio);

            Assert.That(ratio, Is.EqualTo(100f / (CrackerHullMass + CrackerCargoMass)).Within(0.01f),
                "Two 50-rated landing thrusters do not give the cracker the lift their ratings add up to.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull below orbit whose lift is short sinks and is in lift lost. One thruster on the cracker is a ratio of
    /// 0.40 - under the half-lift floor - so it falls at the full rate and the alarms take the ship over.
    /// </summary>
    [Test]
    public async Task PartialLiftSinksAndEntersLiftLost()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var topAir = layers[^2];
        var airMapId = await MapIdOf(pair, topAir);

        var hull = await BuildCracker(pair, airMapId);
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        await AddLandingThrusters(pair, hull, 1);

        // The fall gate sweeps at 2 Hz and the grace period is three seconds.
        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            var mapUid = entMan.GetComponent<TransformComponent>(hull).MapUid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(mapUid), Is.True,
                    $"The under-lifted hull is sitting on {entMan.ToPrettyString(mapUid)} instead of sinking.");
                Assert.That(entMan.HasComponent<WFLiftLostComponent>(hull), Is.True,
                    "A hull sinking under its own weight is not in lift lost.");
                Assert.That(entMan.GetComponent<WFLiftLostComponent>(hull).Ratio, Is.LessThan(1f),
                    "The lift-lost state recorded a ratio that would have flown.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The callouts escalate orbit-to-ground in order and the ship's own situation code comes back afterwards. The
    /// hull starts on yellow, so a restore to green would be the state machine forgetting rather than restoring.
    /// </summary>
    [TestCase(-1)] // No PA installed.
    [TestCase(0)] // PA lost power.
    [TestCase(1)] // Working PA must not get a duplicate global callout.
    public async Task AlarmsEscalateAndRestoreThePriorCode(int speakerPower)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var alerts = server.System<Content.Server._WF.ShipPa.ShipAlertSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMapId = await MapIdOf(pair, orbit);

        var hull = await BuildCracker(pair, orbitMapId);
        await MapInitHull(pair, hull);

        if (speakerPower >= 0)
        {
            await server.WaitPost(() =>
            {
                var speaker = entMan.SpawnEntity("AirAlarm", new EntityCoordinates(hull, new Vector2(7.5f, 4.5f)));
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(speaker, speakerPower == 0);
            });
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
        }
        await server.WaitAssertion(() =>
        {
            var counts = server.System<Content.Server._WF.ShipPa.ShipPaSystem>().CountSpeakers(hull);
            Assert.That(counts.Online, Is.EqualTo(speakerPower == 1 ? 1 : 0));
        });
        var heard = new HashSet<string>();
        var voices = new[] { "dont_sink", "sink_rate", "terrain", "too_low_terrain", "pull_up" };

        await server.WaitPost(() => alerts.SetCode(hull, "ShipCodeYellow", announce: false));

        var seen = new List<string>();

        await server.WaitPost(() => seen.Add(entMan.GetComponent<ShipAlertComponent>(hull).Code.Id));

        // No settle: the caution chime is set inside the call, and one tick of the flight sweep is already past it.
        var refusal = await EnterAtmosphere(pair, hull, settle: 0f);
        Assert.That(refusal, Is.Null, $"The confirmed descent was refused: {refusal}");

        await server.WaitPost(() => seen.Add(entMan.GetComponent<ShipAlertComponent>(hull).Code.Id));

        // A full plummet is four gaps at CE's 0.15 levels/s² up to a 1.2 terminal; sample the code as it goes.
        for (var i = 0; i < 120; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));

            await server.WaitPost(() =>
            {
                if (!entMan.TryGetComponent<ShipAlertComponent>(hull, out var alert))
                    return;

                // Working PA now replicates a timeline for client-side playback instead of server audio entities.
                if (speakerPower == 1 && entMan.TryGetComponent<ShipPaBroadcastComponent>(hull, out var broadcasts))
                {
                    foreach (var broadcast in broadcasts.Broadcasts)
                    foreach (var voice in voices)
                    {
                        if (broadcast.Path != $"/Audio/_WF/PlanetCracker/Flight/{voice}.ogg")
                            continue;
                        Assert.That(broadcast.Length, Is.GreaterThan(0f), "PA voice must resolve playable audio.");
                        heard.Add(voice);
                    }
                }

                var audioQuery = entMan.EntityQueryEnumerator<AudioComponent>();
                while (audioQuery.MoveNext(out var audio))
                {
                    foreach (var voice in voices)
                    {
                        if (audio.FileName != $"/Audio/_WF/PlanetCracker/Flight/{voice}.ogg")
                            continue;
                        heard.Add(voice);
                        Assert.That(audio.Global, Is.EqualTo(speakerPower != 1),
                            "Missing PA needs a hull-wide voice; working PA must not get a duplicate global voice.");
                    }
                }

                var code = alert.Code.Id;

                if (seen.Count == 0 || seen[^1] != code)
                    seen.Add(code);
            });

            if (seen.Count > 0 && seen[^1] == "ShipCodeYellow" && seen.Count > 1)
                break;
        }

        var expected = new[]
        {
            "ShipCodeYellow",
            WFFlightSystem.AlertLiftLost,
            WFFlightSystem.AlertDontSink,
            WFFlightSystem.AlertSinkRate,
            WFFlightSystem.AlertTerrain,
            WFFlightSystem.AlertTooLowTerrain,
            WFFlightSystem.AlertPullUp,
            "ShipCodeYellow",
        };

        Assert.That(seen, Is.EqualTo(expected),
            $"The flight alarms did not walk orbit to ground and back: {string.Join(" -> ", seen)}");

        Assert.That(heard, Is.EquivalentTo(voices), "Every staged GPWS voice must schedule PA playback or produce fallback audio, not just change the alert code.");

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A gliding hull gains speed along its own heading each layer; a hull with none drops straight.</summary>
    [Test]
    public async Task GlideGainsSpeedPerLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var physics = server.System<SharedPhysicsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMapId = await MapIdOf(pair, orbit);

        var hull = await BuildCracker(pair, orbitMapId);
        await MapInitHull(pair, hull);

        var refusal = await EnterAtmosphere(pair, hull);
        Assert.That(refusal, Is.Null, $"The confirmed descent was refused: {refusal}");

        await server.WaitPost(() => physics.SetLinearVelocity(hull, new Vector2(4f, 0f)));

        var start = 0f;
        var startDepth = int.MaxValue;

        await server.WaitAssertion(() =>
        {
            start = entMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(hull).LinearVelocity.Length();
            startDepth = entMan.GetComponent<WFLiftLostComponent>(hull).LastDepth;
        });

        // Two layer crossings is enough to tell a per-layer gain from a one-off.
        var crossed = 0;
        var speed = start;

        for (var i = 0; i < 200 && crossed < 2; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));

            await server.WaitPost(() =>
            {
                if (!entMan.TryGetComponent<WFLiftLostComponent>(hull, out var lost))
                    return;

                if (lost.LastDepth >= startDepth)
                    return;

                crossed = startDepth - lost.LastDepth;
                speed = entMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(hull).LinearVelocity.Length();
            });
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(crossed, Is.GreaterThanOrEqualTo(2), "The hull never fell through two layers.");

            // Two 25% boosts are 1.5625x, and the hull's ordinary airborne damping eats into that over the seconds
            // the fall takes; without the glide the same seconds would have left it BELOW the speed it started at.
            Assert.That(speed, Is.GreaterThan(start * 1.15f),
                $"Two layers of a 25% glide gain left {start} at {speed}, which is not a per-layer gain at all.");
        }

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The landing decision is relative to the speed a free fall actually arrives at. CE zeroes the fall speed at
    /// every layer boundary, so a plummet tops out near 0.47 levels/s and never approaches the 1.2 terminal velocity:
    /// the old fixed 0.8 levels/s threshold called every free fall in the game a hard landing, which is exactly what
    /// the playtest saw. A free fall is a crash, a partial-lift sink lands under the threshold, and landing at all
    /// costs the hull damage.
    /// </summary>
    [Test]
    public async Task FreeFallCrashesAndAPartialLiftSinkHardLands()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var zLevels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var groundMapId = await MapIdOf(pair, ground);

        await LayTiles(pair, ground, new Vector2i(-24, -24), new Vector2i(48, 48));

        var hull = await BuildCracker(pair, groundMapId);
        await MapInitHull(pair, hull);

        // The landing decision is taken purely on the state and the touchdown speed, so both are set by hand rather
        // than flown: flying each branch for real is four gaps of ticks for one boolean.
        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<CEZGridFallerComponent>(hull);
            var lost = entMan.EnsureComponent<WFLiftLostComponent>(hull);
            lost.Ratio = 0.2f;
        });

        var freeFall = 0f;
        var sink = 0f;

        await server.WaitAssertion(() =>
        {
            var faller = entMan.GetComponent<CEZGridFallerComponent>(hull);

            freeFall = zLevels.WfGetFreeFallSpeed(faller);

            // The bottom of the partial-lift band comes down at (1 - r) of gravity through the same one-level gap.
            sink = zLevels.WfGetFreeFallSpeed(
                faller.GridGravity * (1f - CEZLevelsSystem.WFPartialLiftRatio),
                faller.GridTerminalVelocity);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(freeFall, Is.GreaterThan(faller.GridCrashVelocity),
                    "A free fall does not even reach CE's own crash velocity, so nothing would ever crash.");
                Assert.That(freeFall, Is.LessThan(faller.GridTerminalVelocity),
                    "One gap is nowhere near long enough to reach CE's terminal velocity; the reference is wrong.");
                Assert.That(sink, Is.LessThan(freeFall * CEZLevelsSystem.WFHardLandingFraction),
                    $"A half-lifted sink arrives at {sink} against a {freeFall} free fall, which the threshold calls a crash.");
            }
        });

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(hull);
            var faller = entMan.GetComponent<CEZGridFallerComponent>(hull);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(zLevels.WfTryHardLanding((hull, grid, faller), freeFall), Is.False,
                    "A hull that fell the whole way down still walked away from it.");
                Assert.That(zLevels.WfTryHardLanding((hull, grid, faller), sink), Is.True,
                    "A partial-lift sink was blown apart instead of landing hard.");
            }
        });

        await server.WaitAssertion(() =>
        {
            var damage = entMan.GetComponent<WFSkidComponent>(hull).TileDamage;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.EntityExists(hull), Is.True, "The hard-landed hull was destroyed.");
                Assert.That(entMan.HasComponent<WFLiftLostComponent>(hull), Is.False,
                    "A hull that is on the ground is still in lift lost.");
                Assert.That(damage, Is.Not.Empty, "The hard landing cost the hull nothing at all.");
                Assert.That(damage.Values, Is.All.LessThan(WFFlightSystem.SkidTileThreshold),
                    "A hull that came straight down with no speed on lost tiles to the impact alone.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hard landing with planar speed puts the impact into the leading edge and then keeps going, which is what a
    /// hull coming out of the sky is supposed to look like: it grinds its nose off and slides, it does not stop dead.
    /// </summary>
    [Test]
    public async Task HardLandingWithSpeedTearsTheLeadingEdgeAndSlides()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var physics = server.System<SharedPhysicsSystem>();
        var zLevels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var groundMapId = await MapIdOf(pair, ground);

        await LayTiles(pair, ground, new Vector2i(-24, -24), new Vector2i(48, 48));

        var hull = await BuildCracker(pair, groundMapId);
        await MapInitHull(pair, hull);

        var before = 0;

        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<CEZGridFallerComponent>(hull);
            entMan.EnsureComponent<WFLiftLostComponent>(hull).Ratio = 0.6f;

            physics.SetLinearVelocity(hull, new Vector2(WFFlightSystem.SkidRamSpeed + 4f, 0f));
            before = TileCount(entMan, maps, hull);
        });

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(hull);
            var faller = entMan.GetComponent<CEZGridFallerComponent>(hull);
            var impact = zLevels.WfGetFreeFallSpeed(faller) * (CEZLevelsSystem.WFHardLandingFraction - 0.05f);

            Assert.That(zLevels.WfTryHardLanding((hull, grid, faller), impact), Is.True,
                "A touchdown just under the threshold was not a hard landing.");
        });

        await server.WaitAssertion(() =>
        {
            var after = TileCount(entMan, maps, hull);
            var speed = entMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(hull)
                .LinearVelocity.Length();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFSkidComponent>(hull), Is.True, "The hard-landed hull is not skidding.");
                Assert.That(after, Is.LessThan(before),
                    "A hull that came in fast and sideways kept every tile of its leading edge.");
                Assert.That(after, Is.GreaterThan(before / 2),
                    $"The impact took {before - after} of {before} tiles; a hard landing is not supposed to be a crash.");
                Assert.That(speed, Is.GreaterThan(WFFlightSystem.SkidStopSpeed),
                    "The hull stopped dead where it landed instead of sliding out.");
            }
        });

        var touchdown = Vector2.Zero;
        await server.WaitPost(() => touchdown = server.System<SharedTransformSystem>().GetWorldPosition(hull));
        for (var sample = 0; sample < 5; sample++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.2f));
            await server.WaitPost(() =>
            {
                var body = entMan.GetComponent<PhysicsComponent>(hull);
                TestContext.Out.WriteLine($"Skid sample {sample}: speed={body.LinearVelocity}, damping={body.LinearDamping}, skid={entMan.HasComponent<WFSkidComponent>(hull)}, grip={zLevels.GetGroundGrip(hull)}, status={body.BodyStatus}");
            });
        }
        await server.WaitAssertion(() =>
        {
            var travelled = server.System<SharedTransformSystem>().GetWorldPosition(hull) - touchdown;
            Assert.That(travelled.X, Is.GreaterThan(5f),
                "An actual second of physics must carry the wreck forward, not merely leave velocity on the impact tick.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Flying inside an atmosphere makes noise: one looping wind stream aboard every hull below orbit, a second stream
    /// of airframe rumble once the lift goes, the lift-lost caution alarm looping under the callouts for the whole
    /// emergency, and nothing at all left playing once the hull is down.
    /// </summary>
    [Test]
    public async Task AtmosphereLoopsWindAndAFallAddsRumble()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var lowAir = layers[1];
        var lowAirMapId = await MapIdOf(pair, lowAir);

        await LayTiles(pair, ground, new Vector2i(-24, -24), new Vector2i(48, 48));

        var hull = await BuildCracker(pair, lowAirMapId);
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);

        // Three thrusters is a ratio of 1.2, so the hull holds its layer and the wind is the only thing playing.
        var thrusters = await AddLandingThrusters(pair, hull, 3);

        // The ambience sweep is 1 Hz.
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var ambience = entMan.GetComponent<WFFlightAmbienceComponent>(hull);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ambience.Wind, Is.Not.Null, "A hull on an air layer is flying in silence.");
                Assert.That(Streams(entMan, WindClip), Is.EqualTo(1),
                    "A hull on an air layer is not running exactly one wind stream.");
                Assert.That(ambience.Rumble, Is.Null, "A hull that is not falling is rumbling anyway.");
            }
        });

        await server.WaitPost(() =>
        {
            foreach (var thruster in thrusters)
            {
                entMan.DeleteEntity(thruster);
            }
        });

        // The fall gate sweeps at 2 Hz, the ambience behind it at 1 Hz, and the gap itself is only about four seconds
        // of fall, so the state is sampled all the way down rather than at one guessed moment.
        var alarm = EntityUid.Invalid;
        var rumbled = false;
        var wind = 0;

        for (var i = 0; i < 120 && !await OnGround(pair, hull, ground); i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));

            await server.WaitPost(() =>
            {
                if (!entMan.TryGetComponent<WFFlightAmbienceComponent>(hull, out var ambience)
                    || !entMan.TryGetComponent<WFLiftLostComponent>(hull, out var lost))
                {
                    return;
                }

                if (ambience.Rumble is null || lost.Alarm is not { } loop)
                    return;

                var streams = Streams(entMan, WindClip);

                rumbled = true;
                alarm = loop;

                if (streams > wind)
                    wind = streams;
            });
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rumbled, Is.True,
                "A falling hull never carried both the airframe rumble and a looping lift-lost alarm.");
            Assert.That(wind, Is.LessThanOrEqualTo(2),
                $"The falling hull stacked up {wind} wind streams; a re-cut loop must replace the old one, not join it.");
        }

        // A landing is not instant: the skid has to stop and the ambience sweep has to come round again.
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(ground),
                    "The hull never reached the ground, so nothing about the landing was measured.");
                Assert.That(entMan.HasComponent<WFFlightAmbienceComponent>(hull), Is.False,
                    "A landed hull is still flying through wind.");
                Assert.That(Streams(entMan, WindClip), Is.Zero, "The wind loop outlived the landing.");
                Assert.That(Streams(entMan, RumbleClip), Is.Zero, "The fall rumble outlived the landing.");
                Assert.That(entMan.EntityExists(alarm), Is.False, "The lift-lost alarm loop outlived the landing.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The kit marks the existing engine and consumes only the kit.</summary>
    [Test]
    public async Task KitConvertsAThruster()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var interaction = server.System<SharedInteractionSystem>();
        var map = await pair.CreateTestMap();
        var transport = await BuildTransport(pair, map.MapId);
        var thruster = EntityUid.Invalid;
        var user = EntityUid.Invalid;
        var kit = EntityUid.Invalid;
        var coords = default(EntityCoordinates);
        ThrusterComponent original = default!;

        await server.WaitPost(() =>
        {
            foreach (var child in Children(entMan, transport))
            {
                if (entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID == PlainThruster)
                    thruster = child;
            }

            Assert.That(thruster, Is.Not.EqualTo(EntityUid.Invalid));
            original = entMan.GetComponent<ThrusterComponent>(thruster);
            coords = entMan.GetComponent<TransformComponent>(thruster).Coordinates;
            user = entMan.SpawnEntity(ViewerProto, new EntityCoordinates(transport, coords.Position + Vector2.UnitX));
            kit = entMan.SpawnEntity(Kit, new EntityCoordinates(transport, coords.Position + Vector2.UnitX));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitPost(() => interaction.InteractUsing(user, kit, thruster, coords));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(thruster), Is.True, "Conversion replaced the engine entity.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<ThrusterComponent>(thruster), Is.SameAs(original));
                Assert.That(entMan.HasComponent<WFLandingThrusterComponent>(thruster), Is.True);
                Assert.That(entMan.GetComponent<MetaDataComponent>(thruster).EntityPrototype?.ID, Is.EqualTo(PlainThruster));
                Assert.That(entMan.GetComponent<TransformComponent>(thruster).Coordinates, Is.EqualTo(coords));
                Assert.That(entMan.GetComponent<TransformComponent>(thruster).Anchored, Is.True);
                Assert.That(entMan.EntityExists(kit), Is.False);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The confirm gate: an under-lifted hull is refused the descent until it says yes, and a raw descend input out
    /// of orbit is refused outright so the gate cannot be walked around.
    /// </summary>
    [Test]
    public async Task DescentNeedsConfirmAndRawInputIsRefused()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMapId = await MapIdOf(pair, orbit);

        var hull = await BuildCracker(pair, orbitMapId);
        await MapInitHull(pair, hull);

        // A held descend key is the old way down; it now does nothing at all from orbit.
        await HoldDescend(pair, hull);
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                "A raw descend input took the hull out of orbit, bypassing the confirm.");
        });

        var refused = await EnterAtmosphere(pair, hull, confirmed: false);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(refused, Is.Not.Null, "An under-lifted hull was let out of orbit without confirming.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "The refused descent moved the hull anyway.");
            }
        });

        var accepted = await EnterAtmosphere(pair, hull, confirmed: true);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(accepted, Is.Null, $"The confirmed descent was refused: {accepted}");
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(entMan.GetComponent<TransformComponent>(hull).MapUid),
                    Is.True, "The confirmed descent did not put the hull into the gap below orbit.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The refusal is about the layer, not about what holds the hull up. A hull with a working gravity generator
    /// passes CE's own lift gate, which is what put the raw keys back in play: the descend key is ignored, and so is
    /// the climb key, because nothing is above orbit and CE's climb falls back to the gap below when it finds no gap
    /// above - a descent with no lift warning, no confirm and no popup.
    /// </summary>
    [Test]
    public async Task GravgenHullRidesNeitherVerticalKeyOutOfOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMapId = await MapIdOf(pair, orbit);

        var hull = await BuildCracker(pair, orbitMapId);
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);

        // A charged gravity generator's whole effect on the pilot gate is the grid's own gravity; Inherent pins it on
        // the way a working generator holds it, with no charge-up to sit through.
        await server.WaitPost(() =>
        {
            var gravity = entMan.EnsureComponent<GravityComponent>(hull);
            gravity.Enabled = true;
            gravity.Inherent = true;
        });

        var descending = await HoldVertical(pair, hull, ShuttleButtons.DescendZ);
        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                "A hull with a working gravgen rode the descend key out of orbit, past the confirm.");
        });

        await server.WaitPost(() => entMan.DeleteEntity(descending));
        await HoldVertical(pair, hull, ShuttleButtons.AscendZ);
        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                "A hull with a working gravgen rode the climb key out of orbit; there is nothing above orbit to climb to.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull with the lift to fly climbs from the ground all the way into orbit on one latched Liftoff request: every
    /// air layer, the cloud layer, and the last gap into orbit. A pilot whose latch stopped feeding the normal CE
    /// ascent path in a gap, unable to reach orbit, is what this guards against.
    /// </summary>
    [Test]
    public async Task LatchedLiftoffClimbsFromGroundToOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);

        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var orbit = layers[^1];
        var groundMapId = await MapIdOf(pair, ground);

        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(24, 24));
        var hull = await BuildCracker(pair, groundMapId);
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 3);
        var pilot = await HoldVertical(pair, hull, ShuttleButtons.None);
        var console = FindShuttleConsole(entMan, hull);
        var levels = server.System<CEZLevelsSystem>();
        var started = false;
        string? reason = null;

        await server.WaitPost(() => started = levels.WfTryBeginLiftoff(hull, console, pilot, out reason));
        Assert.That(started, Is.True, $"The grounded Liftoff request was refused: {reason}");

        var trail = new List<string>();
        var reached = false;

        // The latch stays engaged the whole way and feeds the same input the old held key did.
        for (var second = 0; second < 45 && !reached; second++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));

            await server.WaitPost(() =>
            {
                var map = entMan.GetComponent<TransformComponent>(hull).MapUid;
                var z = entMan.TryGetComponent(hull, out CEZPhysicsComponent? zPhys) ? zPhys.LocalPosition : float.NaN;
                trail.Add($"{second}s {entMan.ToPrettyString(map)} z={z:F2}");
                reached = map == orbit;
            });
        }

        Assert.That(reached, Is.True, "The hull never reached orbit on latched liftoff: " + string.Join(" | ", trail));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull that has landed on the ground and lifted off again still answers its planar thrusters: a relaunched
    /// shuttle that climbed but could not move sideways is what this guards against.
    /// </summary>
    [Test]
    public async Task RelaunchedHullStillSteers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);

        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var orbitMapId = await MapIdOf(pair, layers[^1]);

        // The transport, the way it is flown: from orbit through the console's own descent, on its own landing
        // thrusters, onto terrain. CE only eases a descent onto ground it can see; a bare test layer is a crash.
        var hull = await BuildTransport(pair, orbitMapId, Vector2.Zero);
        await MapInitHull(pair, hull);
        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(24, 24));

        var refusal = await EnterAtmosphere(pair, hull, confirmed: true);
        Assert.That(refusal, Is.Null, $"Precondition: the descent was refused: {refusal}");

        var descend = await HoldVertical(pair, hull, ShuttleButtons.DescendZ);
        var landed = false;

        for (var second = 0; second < 40 && !landed; second++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitPost(() => landed = entMan.GetComponent<TransformComponent>(hull).MapUid == ground);
        }

        await server.WaitPost(() => entMan.DeleteEntity(descend));
        Assert.That(landed, Is.True, "Precondition: the hull never landed on the ground layer.");
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        var before = Vector2.Zero;
        await server.WaitPost(() => before = transform.GetWorldPosition(hull));

        var pilot = await HoldVertical(pair, hull, ShuttleButtons.StrafeRight);
        var console = FindShuttleConsole(entMan, hull);
        var levels = server.System<CEZLevelsSystem>();
        var started = false;
        string? reason = null;
        await server.WaitPost(() => started = levels.WfTryBeginLiftoff(hull, console, pilot, out reason));
        Assert.That(started, Is.True, $"The landed hull's Liftoff request was refused: {reason}");
        await server.WaitRunTicks(pair.SecondsToTicks(6f));

        await server.WaitAssertion(() =>
        {
            var after = transform.GetWorldPosition(hull);
            var body = entMan.GetComponent<PhysicsComponent>(hull);
            var map = entMan.GetComponent<TransformComponent>(hull).MapUid;
            var state = $"map={entMan.ToPrettyString(map)} type={body.BodyType} status={body.BodyStatus} awake={body.Awake} "
                        + $"damping={body.LinearDamping} mass={body.Mass} fixedRot={body.FixedRotation} vel={body.LinearVelocity} "
                        + $"canCollide={body.CanCollide} shuttleEnabled={entMan.GetComponent<ShuttleComponent>(hull).Enabled}";

            Assert.That((after - before).Length(), Is.GreaterThan(1f),
                $"The relaunched hull did not move sideways under thrust ({before} -> {after}); {state}");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The map id of a z-layer, for the spawners that want one.</summary>
    /// <summary>
    /// A big hull grinding out a hard landing never writes a non-finite number into itself, whether it arrived with
    /// planar speed or came straight down onto its own footprint, and the whole grind is worth a bounded number of
    /// sounds. Neither half is a server-side nicety: a NaN on the grid is a NaN world position for every sound played
    /// on it, which the client's echo pass hands to MathF.Sign and dies on
    /// (AudioEchoSystem.TryProcessAreaSpaceMagnitude), and a sound per crushed thing per tick is the same OpenAL
    /// source exhaustion that already killed a client once (CrashAudioTest).
    /// </summary>
    [Test]
    public async Task SkiddingHullsStayFiniteAndBounded()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var physics = server.System<SharedPhysicsSystem>();
        var transform = server.System<SharedTransformSystem>();
        var zLevels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var groundMapId = await MapIdOf(pair, ground);

        await LayTiles(pair, ground, new Vector2i(-40, -40), new Vector2i(120, 40));

        // Two 15x15 hulls, far enough apart that neither ploughs into the other: one parked, one sliding away.
        var parked = await BuildCracker(pair, groundMapId);
        var sliding = await BuildCracker(pair, groundMapId, new Vector2(60f, 0f));
        var hulls = new[] { parked, sliding };

        await MapInitHull(pair, parked);
        await MapInitHull(pair, sliding);

        await server.WaitPost(() =>
        {
            foreach (var hull in hulls)
            {
                entMan.EnsureComponent<CEZGridFallerComponent>(hull);
                entMan.EnsureComponent<WFLiftLostComponent>(hull).Ratio = 0.6f;
            }

            physics.SetLinearVelocity(parked, Vector2.Zero);
            physics.SetLinearVelocity(sliding, new Vector2(WFFlightSystem.SkidRamSpeed + 4f, 0f));
        });

        var before = new HashSet<EntityUid>();

        await server.WaitAssertion(() =>
        {
            var existing = entMan.EntityQueryEnumerator<AudioComponent>();
            while (existing.MoveNext(out var uid, out _))
            {
                before.Add(uid);
            }

            foreach (var hull in hulls)
            {
                var grid = entMan.GetComponent<MapGridComponent>(hull);
                var faller = entMan.GetComponent<CEZGridFallerComponent>(hull);
                var impact = zLevels.WfGetFreeFallSpeed(faller) * (CEZLevelsSystem.WFHardLandingFraction - 0.05f);

                Assert.That(zLevels.WfTryHardLanding((hull, grid, faller), impact), Is.True,
                    "A touchdown just under the threshold was not a hard landing.");
            }
        });

        var clips = new Dictionary<string, int>();
        var seen = new HashSet<EntityUid>();
        var bad = new HashSet<string>();
        var skidded = false;

        // Several seconds of grinding: many bites of the leading edge for the hull that is moving, and the first tick
        // is already enough for the one that is not to be let go of.
        for (var i = 0; i < 90; i++)
        {
            await server.WaitRunTicks(2);
            await server.WaitPost(() =>
            {
                var query = entMan.EntityQueryEnumerator<AudioComponent>();
                while (query.MoveNext(out var uid, out var audio))
                {
                    if (!before.Contains(uid) && seen.Add(uid))
                        clips[audio.FileName] = clips.GetValueOrDefault(audio.FileName) + 1;
                }

                foreach (var hull in hulls)
                {
                    if (entMan.Deleted(hull))
                    {
                        bad.Add($"{hull} was deleted mid-skid");
                        continue;
                    }

                    var xform = entMan.GetComponent<TransformComponent>(hull);
                    var body = entMan.GetComponent<PhysicsComponent>(hull);
                    var world = transform.GetWorldPosition(xform);
                    var velocity = body.LinearVelocity;

                    if (!float.IsFinite(world.X) || !float.IsFinite(world.Y))
                        bad.Add($"{hull} world position is {world}");

                    if (!double.IsFinite(transform.GetWorldRotation(xform).Theta))
                        bad.Add($"{hull} world rotation is not finite");

                    if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y))
                        bad.Add($"{hull} linear velocity is {velocity}");

                    if (!float.IsFinite(body.AngularVelocity))
                        bad.Add($"{hull} angular velocity is not finite");
                }

                if (entMan.HasComponent<WFSkidComponent>(sliding))
                    skidded = true;
            });
        }

        var heard = new List<string>();
        foreach (var (clip, count) in clips)
        {
            heard.Add($"{clip} x{count}");
        }

        var breakdown = string.Join(", ", heard);
        TestContext.Out.WriteLine($"Skid audio: {seen.Count} audio entities. {breakdown}");

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(bad, Is.Empty,
                    $"A hard landing put a non-finite number on a hull: {string.Join("; ", bad)}. Every sound played " +
                    $"on that hull inherits it, and the client's echo pass throws on the first one it measures.");
                Assert.That(skidded, Is.True,
                    "The hull that landed with planar speed never skidded, so nothing here was measured.");
                Assert.That(entMan.HasComponent<WFSkidComponent>(parked), Is.False,
                    "The hull that landed with no planar speed is still grinding itself down on the spot.");
                Assert.That(seen, Has.Count.LessThanOrEqualTo(AudioBudget),
                    $"Two hulls grinding out a hard landing created {seen.Count} audio entities. Everything heard: " +
                    $"{breakdown}");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapId = MapId.Nullspace;

        await server.WaitPost(() => mapId = entMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }

    /// <summary>How many tiles a hull still has; the leading edge coming off is a drop in this.</summary>
    private static int TileCount(IEntityManager entMan, SharedMapSystem maps, EntityUid grid)
    {
        var count = 0;
        var tiles = maps.GetAllTilesEnumerator(grid, entMan.GetComponent<MapGridComponent>(grid));

        while (tiles.MoveNext(out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>How many audio entities are playing one clip right now; a leaked loop shows up as a second one.</summary>
    private static int Streams(IEntityManager entMan, string clip)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<AudioComponent>();

        while (query.MoveNext(out _, out var audio))
        {
            if (audio.FileName == clip)
                count++;
        }

        return count;
    }

    /// <summary>True once the hull has arrived on the ground layer, however it got there.</summary>
    private static async Task<bool> OnGround(TestPair pair, EntityUid hull, EntityUid ground)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var landed = false;

        await server.WaitPost(() => landed = entMan.GetComponent<TransformComponent>(hull).MapUid == ground);
        return landed;
    }
}
