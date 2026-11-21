#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WG.ColdStorage;
using Content.Server._WG.EntityCensus;
using Content.Server._WG.Tether;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Explosion;
using Content.Shared.CCVar;
using Robust.Server.Physics;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.ColdStorage;

/// <summary>
/// What stasis never pauses, and what wakes or drops a grid in stasis: a map change, a split, a blast, a rope tied to it,
/// and the census that counts it.
/// </summary>
[TestFixture]
[TestOf(typeof(WGGridStasisSystem))]
public sealed class GridStasisHooksTest
{
    private const int Size = 5;

    private sealed record Wreck(EntityUid Grid, EntityUid Item, EntityUid Mouse);

    /// <summary>Spawn points, docks, cryo pods and a deed holder inside a paused container stay awake on a grid in stasis.</summary>
    [Test]
    public async Task ExemptClassesStayAwakeOnAGridInStasis()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var stasis = server.System<WGGridStasisSystem>();
        var map = await pair.CreateTestMap();
        Wreck wreck = default!;
        EntityUid spawn = default, dock = default, pod = default, unit = default, card = default;

        await Enable(pair);
        await server.WaitPost(() =>
        {
            wreck = BuildWreck(pair, map.MapId, new Vector2(0f, 1000f));
            spawn = entMan.SpawnEntity("SpawnPointLatejoin", new EntityCoordinates(wreck.Grid, new Vector2(1.5f, 3.5f)));
            dock = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(wreck.Grid, new Vector2(0.5f, 0.5f)));
            entMan.System<SharedTransformSystem>().Unanchor(dock);
            pod = entMan.SpawnEntity("CryoPod", new EntityCoordinates(wreck.Grid, new Vector2(3.5f, 3.5f)));
            unit = entMan.SpawnEntity("CryogenicSleepUnit", new EntityCoordinates(wreck.Grid, new Vector2(1.5f, 1.5f)));

            card = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(wreck.Grid, new Vector2(2.5f, 2.5f)));
            entMan.AddComponent<ShuttleDeedComponent>(card);
            var containers = entMan.System<SharedContainerSystem>();
            containers.Insert(card, containers.EnsureContainer<Container>(wreck.Item, "wg_test"));
        });

        try
        {
            await Look(pair);
            await server.WaitAssertion(() =>
            {
                Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "Precondition: the wreck sleeps.");
                Assert.Multiple(() =>
                {
                    Assert.That(Paused(entMan, wreck.Item), Is.True, "The container holding the deed was not paused.");
                    Assert.That(Paused(entMan, card), Is.False, "A deed holder was paused with its container.");
                    Assert.That(Paused(entMan, spawn), Is.False, "A spawn point was paused.");
                    Assert.That(Paused(entMan, dock), Is.False, "A dock was paused.");
                    Assert.That(Paused(entMan, pod), Is.False, "A cryo pod was paused.");
                    Assert.That(Paused(entMan, unit), Is.False, "A cryo sleep unit was paused.");
                    Assert.That(Paused(entMan, wreck.Mouse), Is.True, "An ordinary mob stayed awake.");
                });
            });

            // The sweep leaves them be as well.
            await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
            await server.WaitAssertion(() =>
            {
                Assert.That(Paused(entMan, spawn) || Paused(entMan, dock) || Paused(entMan, pod) || Paused(entMan, card), Is.False,
                    "A sweep paused something exempt.");
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WGStasisCVars.Enabled, false));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A grid that changes map in stasis keeps no record of what it paused and nothing stays paused.</summary>
    [Test]
    public async Task AGridThatChangesMapInStasisLeavesNothingPaused()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var stasis = server.System<WGGridStasisSystem>();
        var maps = server.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        Wreck wreck = default!;
        EntityUid otherMap = default;

        await Enable(pair);
        await server.WaitPost(() =>
        {
            wreck = BuildWreck(pair, map.MapId, new Vector2(0f, 1000f));
            otherMap = maps.CreateMap(out _);
        });

        try
        {
            await Look(pair);
            await server.WaitAssertion(() =>
            {
                Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "Precondition: the wreck sleeps.");
                Assert.That(stasis.Grids[wreck.Grid].Paused, Is.Not.Empty, "Precondition: it paused something.");
            });

            await server.WaitPost(() =>
                entMan.System<SharedTransformSystem>().SetCoordinates(wreck.Grid, new EntityCoordinates(otherMap, new Vector2(0f, 1000f))));
            await server.WaitPost(stasis.EvaluateNow);
            await server.WaitRunTicks(1);

            await server.WaitAssertion(() =>
            {
                var rec = stasis.Grids[wreck.Grid];
                Assert.Multiple(() =>
                {
                    Assert.That(rec.State, Is.EqualTo(WGGridStasisState.Awake), "The record outlived the map change.");
                    Assert.That(rec.Paused, Is.Empty, "The record kept a list of paused entities.");
                    Assert.That(Paused(entMan, wreck.Item) || Paused(entMan, wreck.Mouse), Is.False, "Something stayed paused on the new map.");
                });
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WGStasisCVars.Enabled, false));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A split and a blast beside a grid in stasis wake it, and a blast keeps it awake for a while.</summary>
    [Test]
    public async Task ASplitOrABlastWakesAGridInStasis()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var stasis = server.System<WGGridStasisSystem>();
        var maps = server.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        var origin = new Vector2(0f, 1000f);
        Wreck wreck = default!;

        await Enable(pair);
        await server.WaitPost(() => wreck = BuildWreck(pair, map.MapId, origin));

        try
        {
            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "Precondition: the wreck sleeps.");

            await server.WaitAssertion(() =>
            {
                EntityUid piece = maps.CreateGridEntity(map.MapId);
                var split = new GridSplitEvent(new[] { piece }, wreck.Grid);
                entMan.EventBus.RaiseLocalEvent(wreck.Grid, ref split, true);

                Assert.That(stasis.IsInStasis(wreck.Grid), Is.False, "A split left the grid in stasis.");
                Assert.That(Paused(entMan, wreck.Item) || Paused(entMan, wreck.Mouse), Is.False, "A split left things paused.");
            });

            // The split holds the first wreck awake, so the blast gets one of its own.
            origin += new Vector2(400f, 0f);
            await server.WaitPost(() => wreck = BuildWreck(pair, map.MapId, origin));
            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "Precondition: the second wreck sleeps.");

            await server.WaitAssertion(() =>
            {
                var blast = new ExplosionShockwaveEvent(new MapCoordinates(origin + new Vector2(2.5f, 2.5f), map.MapId), 3, null);
                entMan.EventBus.RaiseEvent(EventSource.Local, ref blast);

                Assert.That(stasis.IsInStasis(wreck.Grid), Is.False, "A blast left the grid in stasis.");
                Assert.That(Paused(entMan, wreck.Item) || Paused(entMan, wreck.Mouse), Is.False, "A blast left things paused.");
            });

            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.False, "The grid slept inside the hold a blast gives it.");
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WGStasisCVars.Enabled, false));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A rope tied between two grids in stasis wakes and pins both; breaking it lets them sleep again.</summary>
    [Test]
    public async Task ARopePinsBothGridsUntilItBreaks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var stasis = server.System<WGGridStasisSystem>();
        var ropes = server.System<RopeSystem>();
        var map = await pair.CreateTestMap();
        Wreck a = default!, b = default!;

        await Enable(pair);
        await server.WaitPost(() =>
        {
            a = BuildWreck(pair, map.MapId, new Vector2(0f, 1000f));
            b = BuildWreck(pair, map.MapId, new Vector2(Size + 3f, 1000f));
        });

        try
        {
            await Look(pair);
            Assert.That(stasis.IsInStasis(a.Grid) && stasis.IsInStasis(b.Grid), Is.True, "Precondition: both sleep.");

            EntityUid? rope = null;
            await server.WaitAssertion(() =>
            {
                var pointA = entMan.SpawnEntity("WGRopeAttachPointDebug", new EntityCoordinates(a.Grid, new Vector2(4.5f, 2.5f)));
                var pointB = entMan.SpawnEntity("WGRopeAttachPointDebug", new EntityCoordinates(b.Grid, new Vector2(0.5f, 2.5f)));
                Assert.That(ropes.TryCreateRope(pointA, pointB, "WGRopeDebug", 10f, out rope), Is.True);
                Assert.That(stasis.IsInStasis(a.Grid) || stasis.IsInStasis(b.Grid), Is.False, "Tying a rope left a grid in stasis.");
                Assert.That(stasis.IsPinned(a.Grid) && stasis.IsPinned(b.Grid), Is.True, "A rope pinned no grid.");
            });

            await Look(pair);
            Assert.That(stasis.IsInStasis(a.Grid) || stasis.IsInStasis(b.Grid), Is.False, "A grid with a rope on it slept.");

            await server.WaitPost(() => ropes.BreakRope(rope!.Value));
            await server.WaitAssertion(() =>
                Assert.That(stasis.IsPinned(a.Grid) || stasis.IsPinned(b.Grid), Is.False, "A broken rope left a pin behind."));

            await Look(pair);
            Assert.That(stasis.IsInStasis(a.Grid) && stasis.IsInStasis(b.Grid), Is.True, "The grids stayed awake after the rope broke.");
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WGStasisCVars.Enabled, false));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The census counts grids in stasis and the entities on them, and none with stasis off.</summary>
    [Test]
    public async Task TheCensusCountsGridsInStasis()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var stasis = server.System<WGGridStasisSystem>();
        var census = server.System<WGEntityCensusSystem>();
        var map = await pair.CreateTestMap();
        Wreck wreck = default!;

        await Enable(pair);
        await server.WaitPost(() => wreck = BuildWreck(pair, map.MapId, new Vector2(0f, 1000f)));

        try
        {
            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "Precondition: the wreck sleeps.");

            WGCensus asleep = default!;
            await server.WaitPost(() => asleep = census.TakeNow());
            Assert.Multiple(() =>
            {
                Assert.That(asleep.StasisGrids, Is.GreaterThanOrEqualTo(1), "The census found no grid in stasis.");
                Assert.That(asleep.StasisEntities, Is.GreaterThanOrEqualTo(2), "The census didn't count what sits on it.");
            });

            await server.WaitPost(() => server.CfgMan.SetCVar(WGStasisCVars.Enabled, false));
            WGCensus awake = default!;
            await server.WaitPost(() => awake = census.TakeNow());
            Assert.Multiple(() =>
            {
                Assert.That(awake.StasisGrids, Is.EqualTo(0), "The census counted a grid with stasis off.");
                Assert.That(awake.StasisEntities, Is.EqualTo(0), "The census counted entities with stasis off.");
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WGStasisCVars.Enabled, false));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Turns stasis on with no idle wait, no budget limit and a one-second sweep; NPCs stay put.</summary>
    private static async Task Enable(TestPair pair)
    {
        await pair.Server.WaitPost(() =>
        {
            var cfg = pair.Server.CfgMan;
            cfg.SetCVar(CCVars.NPCEnabled, false);
            cfg.SetCVar(WGStasisCVars.Idle, 0f);
            cfg.SetCVar(WGStasisCVars.Budget, 1000f);
            cfg.SetCVar(WGStasisCVars.Sweep, 1f);
            cfg.SetCVar(WGStasisCVars.Enabled, true);
        });
    }

    /// <summary>Has the system look at every grid a few times, a tick apart: enough to notice, idle and pause.</summary>
    private static async Task Look(TestPair pair, int times = 3)
    {
        var stasis = pair.Server.System<WGGridStasisSystem>();
        for (var i = 0; i < times; i++)
        {
            await pair.Server.WaitPost(stasis.EvaluateNow);
            await pair.Server.WaitRunTicks(1);
        }
    }

    /// <summary>A square hull with a loose item and a mouse on it; call on the server thread.</summary>
    private static Wreck BuildWreck(TestPair pair, MapId mapId, Vector2 position)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(mapId);
        pair.Server.System<SharedTransformSystem>().SetLocalPosition(grid.Owner, position);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < Size; x++)
        for (var y = 0; y < Size; y++)
            tiles.Add((new Vector2i(x, y), pair.TestMap!.Tile.Tile));

        maps.SetTiles(grid.Owner, grid.Comp, tiles);
        var item = entMan.SpawnEntity("SheetSteel1", new EntityCoordinates(grid.Owner, new Vector2(2.5f, 2.5f)));
        var mouse = entMan.SpawnEntity("MobMouse", new EntityCoordinates(grid.Owner, new Vector2(3.5f, 2.5f)));
        return new Wreck(grid.Owner, item, mouse);
    }

    private static bool Paused(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityPaused;
    }
}
