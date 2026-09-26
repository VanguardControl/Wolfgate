#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Caverns;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;
using ClimbDoAfter = Content.Shared.DoAfter.DoAfter;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>Climbing: up from a climb point onto solid ground, down a shaft unhurt, refused under a hull, slower in high gravity.</summary>
[TestFixture]
[TestOf(typeof(WFCavernClimbSystem))]
public sealed class CavernClimbTest
{
    /// <summary>A breathable world with a 2x2 gate whose climb side is south.</summary>
    private const string Surface = "WFSurfaceAsclepiu";

    /// <summary>The 3 g world, whose climb is clamped to 2.5 times the base.</summary>
    private const string HeavySurface = "WFSurfaceAerumna";

    /// <summary>How long a climber must stay put on the exit after arriving.</summary>
    private const int StayTicks = 120;

    /// <summary>Climbs up from beside the gate's climb point: lands on the solid lip, at rest, and stays there.</summary>
    [Test]
    public async Task ClimbUpLandsOnSolidExitAndStays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var gate = await Gate(pair, world);
            var climb = await ClimbPointOf(pair, world, gate);
            var mob = await SpawnSettled(pair, world.Cavern, Beside(gate));
            var delay = await DelayOf(pair, climb);

            await StartVerb(pair, climb, mob, "wf-cavern-verb-climb-up");
            await server.WaitRunTicks(pair.SecondsToTicks(delay) + 10);

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var index = TileOf(pair, world.Ground, mob);

                Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Ground),
                    "The climber never reached the ground.");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(maps.TryGetTileRef(world.Ground, grid, index, out var tile) && !tile.Tile.IsEmpty, Is.True,
                        $"The climber came out over empty ground at {index}.");
                    Assert.That(ground.Shades.ContainsKey(index), Is.False, $"The climber came out over the hole at {index}.");
                    Assert.That(index, Is.EqualTo(ClimbTile(gate)), "The nearest exit is the lip over the climb point.");
                    Assert.That(entMan.GetComponent<CEZPhysicsComponent>(mob).LocalPosition, Is.LessThan(0.1f),
                        "The climber arrived above the ground instead of standing on it.");
                }
            });

            var slip = await FirstTickOff(pair, mob, world.Ground, StayTicks);
            Assert.That(slip, Is.Null, $"The climber left the ground or rose off it: {slip}");
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Climbs down the gate from its lip: no damage, no knockdown, standing on the pad beside the climb point.</summary>
    [Test]
    public async Task ClimbDownIsHarmless()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var biomes = server.System<BiomeSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var gate = await Gate(pair, world);
            var shade = EntityUid.Invalid;
            await server.WaitPost(() => shade = entMan.GetComponent<WFCavernGroundComponent>(world.Ground).Shades[gate.Origin]);

            var mob = await SpawnSettled(pair, world.Ground, ClimbTile(gate));

            await StartVerb(pair, shade, mob, "wf-cavern-verb-climb-down");
            await server.WaitRunTicks(pair.SecondsToTicks(SharedWFCavernClimbSystem.ClimbDownSeconds) + 10);

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var biome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
                var index = TileOf(pair, world.Cavern, mob);
                var fromClimb = index - ClimbTile(gate);

                Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Cavern),
                    "The climber never reached the cavern.");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<DamageableComponent>(mob).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                        "Climbing down hurt the climber.");
                    Assert.That(entMan.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(MobState.Alive));
                    Assert.That(entMan.HasComponent<KnockedDownComponent>(mob), Is.False, "Climbing down knocked the climber down.");
                    Assert.That(Math.Max(Math.Abs(fromClimb.X), Math.Abs(fromClimb.Y)), Is.LessThanOrEqualTo(1),
                        $"The climber arrived at {index}, not beside the climb point at {ClimbTile(gate)}.");
                    Assert.That(maps.TryGetTileRef(world.Cavern, grid, index, out var tile) && !tile.Tile.IsEmpty, Is.True,
                        "The climber arrived over empty cavern floor.");
                    Assert.That(biomes.WfIsPinned(biome, index), Is.True, "The climber arrived off the pinned pad.");
                    Assert.That(entMan.GetComponent<CEZPhysicsComponent>(mob).LocalPosition, Is.LessThan(0.1f),
                        "The climber arrived above the cavern floor instead of standing on it.");
                }
            });

            var slip = await FirstTickOff(pair, mob, world.Cavern, StayTicks);
            Assert.That(slip, Is.Null, $"The climber did not stay on the pad: {slip}");

            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<DamageableComponent>(mob).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                    "The climber was hurt after arriving."));
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>With a grid over every exit tile, the climb fails with the hull popup and the climber stays below.</summary>
    [Test]
    public async Task ClimbRefusedUnderHull()
    {
        // Connected, so the refusal popup reaches a client that can record it.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var probe = pair.Client.System<WFCavernPopupProbeSystem>();
        var hullText = pair.Client.ResolveDependency<ILocalizationManager>().GetString("wf-cavern-climb-blocked-hull");

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var gate = await Gate(pair, world);
            var climb = await ClimbPointOf(pair, world, gate);
            var mob = await PlanetFixture.AttachViewer(pair, world.Cavern, TileCentre(Beside(gate)));
            await pair.RunTicksSync(pair.SecondsToTicks(1f));

            // Every tile within reach of the climb point, the hole's included.
            var reach = new Vector2i(WFCavernClimbSystem.ExitReach, WFCavernClimbSystem.ExitReach);
            var from = ClimbTile(gate) - reach;
            var hull = await PlanetFixture.BuildDebris(pair, await MapIdOf(pair, world.Ground),
                size: 2 * WFCavernClimbSystem.ExitReach + 1, offset: new Vector2(from.X, from.Y));

            // Parked, as a stopped hull is static: a dynamic one is shoved out of the outcrops the viewer's load put there.
            await server.WaitPost(() =>
            {
                server.System<SharedPhysicsSystem>().SetBodyType(hull, BodyType.Static);
                server.System<SharedTransformSystem>().SetLocalPosition(hull, new Vector2(from.X, from.Y));
            });
            await pair.RunTicksSync(pair.SecondsToTicks(1f));

            await server.WaitAssertion(() =>
            {
                var hullXform = entMan.GetComponent<TransformComponent>(hull);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(hullXform.MapUid, Is.EqualTo(world.Ground), "Precondition: the hull is not on the ground map.");
                    Assert.That(hullXform.LocalPosition, Is.EqualTo(new Vector2(from.X, from.Y)),
                        "Precondition: the hull moved off the exit tiles.");
                    Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Cavern),
                        "Precondition: the climber is not in the cavern.");
                }
            });

            var delay = await DelayOf(pair, climb);
            await pair.Client.WaitPost(probe.Received.Clear);
            var doAfter = await StartVerb(pair, climb, mob, "wf-cavern-verb-climb-up");
            await pair.RunTicksSync(pair.SecondsToTicks(delay) + 10);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(doAfter.Completed && !doAfter.Cancelled, Is.True,
                        "Precondition: the climb never finished, so nothing was refused.");
                    Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Cavern),
                        "The climber went up through a hull parked over the exit.");
                }
            });

            await pair.Client.WaitAssertion(() =>
                Assert.That(probe.Received, Does.Contain(hullText),
                    $"The refused climb did not show the hull popup. Received: {string.Join(" | ", probe.Received)}"));
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The climb takes the base time times the clamped surface gravity: 4 s on Asclepiu, 10 s at Aerumna's 3 g.</summary>
    [Test]
    public async Task DelayScalesWithGravity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var tick = (float) server.Timing.TickPeriod.TotalSeconds;

        await EnableCaverns(pair);

        foreach (var (surfaceId, expected) in new[] { (Surface, 4f), (HeavySurface, 10f) })
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var gate = await Gate(pair, world);
                var climb = await ClimbPointOf(pair, world, gate);
                var delay = await DelayOf(pair, climb);

                Assert.That(delay, Is.EqualTo(expected).Within(tick), $"{surfaceId}: the climb point's delay is {delay} s.");

                if (surfaceId != HeavySurface)
                    continue;

                // Aerumna's air is toxic, and the first point of damage would break the climb.
                var mob = await SpawnSettled(pair, world.Cavern, Beside(gate));
                await server.WaitPost(() => server.System<SharedGodmodeSystem>().EnableGodmode(mob));

                var doAfter = await StartVerb(pair, climb, mob, "wf-cavern-verb-climb-up");
                Assert.That(doAfter.Args.Delay.TotalSeconds, Is.EqualTo(expected).Within(tick),
                    $"{surfaceId}: the climb DoAfter lasts {doAfter.Args.Delay.TotalSeconds} s.");

                // By the game clock each tick runs at, not a tick count: the verb runs between ticks, and a 1/30 s tick
                // rounds down to whole TimeSpan ticks, so a count reads up to two ticks long.
                var ticks = 0;
                var arrived = false;
                var arrivedAt = TimeSpan.Zero;
                var budget = pair.SecondsToTicks(expected) + 30;

                while (!arrived && ticks < budget)
                {
                    var tickTime = TimeSpan.Zero;
                    await server.WaitPost(() => tickTime = server.Timing.CurTime);
                    await server.WaitRunTicks(1);
                    ticks++;
                    await server.WaitPost(() => arrived = entMan.GetComponent<TransformComponent>(mob).MapUid == world.Ground);
                    arrivedAt = tickTime;
                }

                Assert.That(arrived, Is.True, $"{surfaceId}: the climber never reached the ground within {budget} ticks.");

                var elapsed = arrivedAt - doAfter.StartTime;
                Assert.That(elapsed, Is.GreaterThanOrEqualTo(doAfter.Args.Delay).And.LessThan(doAfter.Args.Delay + server.Timing.TickPeriod),
                    $"{surfaceId}: the climb finished {elapsed.TotalSeconds} s after it started; its delay is {doAfter.Args.Delay.TotalSeconds} s.");
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The pad tile south of the climb point, away from the hole; the test worlds climb out on the south side.</summary>
    private static Vector2i Beside(WFCavernMouth gate)
    {
        return ClimbTile(gate) + new Vector2i(0, -1);
    }

    /// <summary>The gate's climb point in the cavern.</summary>
    private static async Task<EntityUid> ClimbPointOf(TestPair pair, World world, WFCavernMouth gate)
    {
        var climb = EntityUid.Invalid;

        await pair.Server.WaitPost(() =>
            climb = pair.Server.EntMan.GetComponent<WFCavernGroundComponent>(world.Ground).ClimbPoints[gate.ClimbTile]);
        return climb;
    }

    /// <summary>A climb point's stamped delay in seconds.</summary>
    private static async Task<float> DelayOf(TestPair pair, EntityUid climb)
    {
        var delay = 0f;

        await pair.Server.WaitPost(() => delay = pair.Server.EntMan.GetComponent<WFCavernClimbComponent>(climb).Delay);
        return delay;
    }

    /// <summary>Spawns a human on a tile and lets it settle, failing unless it stands there at rest.</summary>
    private static async Task<EntityUid> SpawnSettled(TestPair pair, EntityUid map, Vector2i tile)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mob = EntityUid.Invalid;

        await server.WaitPost(() => mob = entMan.SpawnEntity(PlanetFixture.ViewerProto, new EntityCoordinates(map, TileCentre(tile))));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(map),
                    $"Precondition: the climber fell off {entMan.ToPrettyString(map)} before climbing.");
                Assert.That(entMan.GetComponent<CEZPhysicsComponent>(mob).LocalPosition, Is.LessThan(0.05f),
                    "Precondition: the climber is not standing on the floor.");
            }
        });

        return mob;
    }

    /// <summary>Runs a climb verb as the user and returns the DoAfter it started, failing if it isn't offered or starts none.</summary>
    private static async Task<ClimbDoAfter> StartVerb(TestPair pair, EntityUid target, EntityUid user, string key)
    {
        var server = pair.Server;
        var verbs = server.System<SharedVerbSystem>();
        var text = server.ResolveDependency<ILocalizationManager>().GetString(key);
        ClimbDoAfter? started = null;

        await server.WaitAssertion(() =>
        {
            var verb = verbs.GetLocalVerbs(target, user, typeof(AlternativeVerb)).FirstOrDefault(v => v.Text == text);

            Assert.That(verb, Is.Not.Null, $"The climber was not offered \"{text}\".");
            verbs.ExecuteVerb(verb!, user, target);
            started = server.EntMan.GetComponent<DoAfterComponent>(user).DoAfters.Values.SingleOrDefault();
            Assert.That(started, Is.Not.Null, $"\"{text}\" did not start exactly one DoAfter.");
        });

        return started!;
    }

    /// <summary>Ticks one at a time; describes the first tick the mob is off the map or off its floor, or null if it never is.</summary>
    private static async Task<string?> FirstTickOff(TestPair pair, EntityUid mob, EntityUid map, int ticks)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        string? off = null;

        for (var tick = 0; tick < ticks && off == null; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                var at = entMan.GetComponent<TransformComponent>(mob).MapUid;
                var height = entMan.GetComponent<CEZPhysicsComponent>(mob).LocalPosition;

                if (at != map || height >= 0.1f)
                    off = $"tick {tick}: on {entMan.ToPrettyString(at)} at height {height}";
            });
        }

        return off;
    }

    /// <summary>The tile an entity stands on in a map.</summary>
    private static Vector2i TileOf(TestPair pair, EntityUid map, EntityUid uid)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();

        return maps.TileIndicesFor(map, entMan.GetComponent<MapGridComponent>(map), entMan.GetComponent<TransformComponent>(uid).Coordinates);
    }
}

/// <summary>Records the entity popups a connected test client receives from the server.</summary>
public sealed partial class WFCavernPopupProbeSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    /// <summary>Every popup message received, oldest first.</summary>
    public readonly List<string> Received = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        if (!_net.IsClient)
            return;

        SubscribeNetworkEvent<PopupEntityEvent>(ev => Received.Add(ev.Message));
    }
}
