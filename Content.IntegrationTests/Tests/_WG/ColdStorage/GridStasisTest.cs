#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._WG.ColdStorage;
using Content.Server.Atmos.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.CCVar;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Mind;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.ColdStorage;

/// <summary>
/// A grid in space nobody attends is paused where it stands, walls and the grid itself excepted, and wakes whole the
/// tick someone comes near; minds aboard, stations, deeds, pins, holds, arrivals and docks decide who may sleep.
/// </summary>
[TestFixture]
[TestOf(typeof(WGGridStasisSystem))]
public sealed class GridStasisTest
{
    private const int Size = 5;
    private static readonly Vector2 Far = new(5000f, 5000f);

    private sealed record Wreck(EntityUid Grid, EntityUid Wall, EntityUid Item, EntityUid Mouse);

    /// <summary>A wreck sleeps with nobody near, its atmosphere stops, and a viewer wakes it whole; ranges have hysteresis.</summary>
    [Test]
    public async Task AWreckNobodyIsNearSleepsAndWakesForAViewer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var stasis = server.System<WGGridStasisSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var viewer = await PlanetFixture.AttachViewer(pair, map.MapUid, Far);
        var origin = new Vector2(300f, 0f);
        Wreck wreck = default!;

        await Enable(pair);
        await server.WaitPost(() =>
        {
            wreck = BuildWreck(pair, map.MapId, origin);
            entMan.EnsureComponent<GridAtmosphereComponent>(wreck.Grid);
        });
        await server.WaitRunTicks(5);

        try
        {
            await Look(pair);
            await server.WaitAssertion(() =>
            {
                Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "A wreck nobody is near stayed awake.");
                Assert.That(entMan.GetComponent<TransformComponent>(wreck.Wall).Anchored, Is.True, "Precondition: the wall is anchored.");
                Assert.Multiple(() =>
                {
                    Assert.That(Paused(entMan, wreck.Grid), Is.False, "The grid entity itself was paused.");
                    Assert.That(Paused(entMan, wreck.Wall), Is.False, "An anchored wall was paused.");
                    Assert.That(Paused(entMan, wreck.Item), Is.True, "A loose item stayed awake.");
                    Assert.That(Paused(entMan, wreck.Mouse), Is.True, "A mouse stayed awake.");
                });
            });

            // Atmos leaves a grid in stasis alone.
            var counter = 0;
            await server.WaitPost(() => counter = entMan.GetComponent<GridAtmosphereComponent>(wreck.Grid).UpdateCounter);
            await server.WaitRunTicks(60);
            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<GridAtmosphereComponent>(wreck.Grid).UpdateCounter, Is.EqualTo(counter), "Atmos ran on a grid in stasis."));

            // Past the wake range but inside the stash range, a sleeping wreck sleeps on.
            var between = (stasis.WakeRange + stasis.StashRange) / 2f;
            await Move(pair, viewer, map.MapUid, At(origin, between));
            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "The wreck woke for someone outside the wake range.");

            // Someone about to see it: everything wakes the tick it is looked at.
            await Move(pair, viewer, map.MapUid, At(origin, stasis.UrgentRange - 4f));
            await server.WaitPost(stasis.EvaluateNow);
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                Assert.That(stasis.Grids[wreck.Grid].State, Is.EqualTo(WGGridStasisState.Awake), "The wreck didn't wake whole in one tick.");
                Assert.That(Paused(entMan, wreck.Item) || Paused(entMan, wreck.Mouse), Is.False, "Something stayed paused after waking.");
                counter = entMan.GetComponent<GridAtmosphereComponent>(wreck.Grid).UpdateCounter;
            });
            await server.WaitRunTicks(60);
            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<GridAtmosphereComponent>(wreck.Grid).UpdateCounter, Is.GreaterThan(counter), "Atmos didn't resume after waking."));

            // Back between the ranges, an awake wreck stays awake.
            await Move(pair, viewer, map.MapUid, At(origin, between));
            await Look(pair);
            Assert.That(stasis.Grids[wreck.Grid].State, Is.EqualTo(WGGridStasisState.Awake), "The wreck slept with someone inside the stash range.");

            // Gone again: it sleeps, and the sweep pauses what turns up on it later.
            await Move(pair, viewer, map.MapUid, Far);
            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "The wreck didn't sleep once the viewer left.");
            var newcomer = EntityUid.Invalid;
            await server.WaitPost(() => newcomer = entMan.SpawnEntity("SheetSteel1", new EntityCoordinates(wreck.Grid, new Vector2(1.5f, 3.5f))));
            await server.WaitAssertion(() => Assert.That(Paused(entMan, newcomer), Is.False, "Precondition: a new item starts awake."));
            await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
            await server.WaitAssertion(() => Assert.That(Paused(entMan, newcomer), Is.True, "The sweep left a newcomer awake."));

            // Turned off, everything wakes at once.
            await server.WaitPost(() => server.CfgMan.SetCVar(WGStasisCVars.Enabled, false));
            await server.WaitAssertion(() =>
            {
                Assert.That(stasis.IsInStasis(wreck.Grid), Is.False);
                Assert.That(Paused(entMan, wreck.Item) || Paused(entMan, wreck.Mouse) || Paused(entMan, newcomer), Is.False, "Turning stasis off left things paused.");
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WGStasisCVars.Enabled, false));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A mind aboard keeps a grid and its docked neighbours awake, but never through a station; stations and deeded ships stay out.</summary>
    [Test]
    public async Task MindsStationsDeedsAndDocksDecideWhoSleeps()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var stasis = server.System<WGGridStasisSystem>();
        var map = await pair.CreateTestMap();
        Wreck minded = default!, partner = default!, station = default!, berthed = default!, ship = default!;
        var body = EntityUid.Invalid;
        var stationBody = EntityUid.Invalid;

        await Enable(pair);
        await server.WaitPost(() =>
        {
            minded = BuildWreck(pair, map.MapId, new Vector2(0f, 1000f));
            partner = BuildWreck(pair, map.MapId, new Vector2(Size, 1000f));
            Dock(entMan, minded.Grid, partner.Grid);

            station = BuildWreck(pair, map.MapId, new Vector2(0f, 2000f));
            entMan.AddComponent<StationMemberComponent>(station.Grid);
            berthed = BuildWreck(pair, map.MapId, new Vector2(Size, 2000f));
            Dock(entMan, station.Grid, berthed.Grid);

            ship = BuildWreck(pair, map.MapId, new Vector2(0f, 3000f));
            entMan.AddComponent<ShuttleDeedComponent>(ship.Grid);

            var minds = entMan.System<SharedMindSystem>();
            body = entMan.SpawnEntity("MobHuman", new EntityCoordinates(minded.Grid, new Vector2(1.5f, 1.5f)));
            minds.TransferTo(minds.CreateMind(null), body);
            stationBody = entMan.SpawnEntity("MobHuman", new EntityCoordinates(station.Grid, new Vector2(1.5f, 1.5f)));
            minds.TransferTo(minds.CreateMind(null), stationBody);
        });

        try
        {
            await Look(pair);
            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(stasis.IsInStasis(minded.Grid), Is.False, "A grid with a mind aboard went into stasis.");
                    Assert.That(Paused(entMan, body), Is.False, "A body with a mind was paused.");
                    Assert.That(stasis.IsInStasis(partner.Grid), Is.False, "A grid docked to an attended one went into stasis.");
                    Assert.That(stasis.Grids[station.Grid].Policy, Is.EqualTo(WGStasisPolicy.Station));
                    Assert.That(stasis.IsInStasis(station.Grid), Is.False, "A station went into stasis.");
                    Assert.That(stasis.IsInStasis(berthed.Grid), Is.True, "A ship docked to an attended station was kept awake by it.");
                    Assert.That(stasis.Grids[ship.Grid].Policy, Is.EqualTo(WGStasisPolicy.PlayerShip));
                    Assert.That(stasis.IsInStasis(ship.Grid), Is.False, "A deeded ship went into stasis with player ships off.");
                });
            });

            // The mind leaves: the docked pair sleeps together. A dark player ship sleeps once player ships may.
            await server.WaitPost(() =>
            {
                entMan.DeleteEntity(body);
                server.CfgMan.SetCVar(WGStasisCVars.PlayerShips, true);
            });
            await Look(pair);
            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(stasis.IsInStasis(minded.Grid), Is.True, "The grid stayed awake after its mind left.");
                    Assert.That(stasis.IsInStasis(partner.Grid), Is.True, "Its docked partner stayed awake.");
                    Assert.That(stasis.IsInStasis(ship.Grid), Is.True, "A player ship with no power stayed awake with player ships on.");
                    Assert.That(stasis.IsInStasis(station.Grid), Is.False, "A station went into stasis with stations off.");
                });
            });

            // A station sleeps once stations may, and only once the mind aboard is gone.
            await server.WaitPost(() => server.CfgMan.SetCVar(WGStasisCVars.Stations, true));
            await Look(pair);
            await server.WaitAssertion(() => Assert.That(stasis.IsInStasis(station.Grid), Is.False, "A station with a mind aboard went into stasis."));
            await server.WaitPost(() => entMan.DeleteEntity(stationBody));
            await Look(pair);
            await server.WaitAssertion(() => Assert.That(stasis.IsInStasis(station.Grid), Is.True, "An empty station stayed awake with stations on."));
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(WGStasisCVars.Stations, false);
                server.CfgMan.SetCVar(WGStasisCVars.PlayerShips, false);
                server.CfgMan.SetCVar(WGStasisCVars.Enabled, false);
            });
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Pins, holds from <see cref="WGGridStasisSystem.Wake"/> and announced arrivals wake a grid and keep it awake.</summary>
    [Test]
    public async Task PinsHoldsAndArrivalsKeepAGridAwake()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var stasis = server.System<WGGridStasisSystem>();
        var map = await pair.CreateTestMap();
        var origin = new Vector2(0f, 1000f);
        Wreck wreck = default!;

        await Enable(pair);
        await server.WaitPost(() => wreck = BuildWreck(pair, map.MapId, origin));

        try
        {
            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "Precondition: the wreck sleeps.");

            // A pin wakes it on the spot and holds it until let go.
            await server.WaitAssertion(() =>
            {
                stasis.Pin(wreck.Grid, "test");
                Assert.That(stasis.IsInStasis(wreck.Grid), Is.False, "Pinning didn't wake the grid.");
                Assert.That(Paused(entMan, wreck.Item), Is.False, "Pinning left things paused.");
            });
            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.False, "A pinned grid went into stasis.");
            await server.WaitAssertion(() =>
            {
                Assert.That(stasis.Unpin(wreck.Grid, "test"), Is.True, "Letting go of a held pin said it wasn't held.");
                Assert.That(stasis.Unpin(wreck.Grid, "test"), Is.False, "Letting go of a pin twice said it was held.");
            });
            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "The grid stayed awake after its pin was let go.");

            // Wake holds it for as long as asked, then lets it sleep.
            await server.WaitAssertion(() =>
            {
                Assert.That(stasis.Wake(wreck.Grid, TimeSpan.FromSeconds(2)), Is.True);
                Assert.That(Paused(entMan, wreck.Mouse), Is.False, "Wake left things paused.");
            });
            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.False, "The grid slept inside its hold.");
            await server.WaitRunTicks(pair.SecondsToTicks(2.5f));
            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "The grid stayed awake past its hold.");

            // Someone announced to arrive beside it wakes it.
            await server.WaitPost(() => stasis.NoteArrival(new MapCoordinates(origin, map.MapId), TimeSpan.FromSeconds(10)));
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                Assert.That(stasis.IsInStasis(wreck.Grid), Is.False, "An arrival didn't wake the grid.");
                Assert.That(Paused(entMan, wreck.Item), Is.False, "An arrival left things paused.");
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WGStasisCVars.Enabled, false));
        }

        await server.WaitAssertion(() => Assert.That(stasis.Wake(wreck.Grid), Is.False, "Wake said it worked with stasis off."));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A grid deleted after it began going into stasis is dropped without breaking the tick, and a grid whose map is
    /// paused while it wakes over several ticks has nothing unpaused on that map.
    /// </summary>
    [Test]
    public async Task DeletedGridsAndPausedMapsAreLeftAlone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var stasis = server.System<WGGridStasisSystem>();
        var maps = server.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        var viewer = await PlanetFixture.AttachViewer(pair, map.MapUid, Far);
        var origin = new Vector2(300f, 0f);
        var wakeRange = server.CfgMan.GetCVar(WGStasisCVars.WakeRange);
        Wreck doomed = default!, wreck = default!;

        await Enable(pair);
        try
        {
            // With no budget, the grid is marked as going into stasis but nothing on it is gathered yet.
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(WGStasisCVars.Budget, 0f);
                doomed = BuildWreck(pair, map.MapId, origin);
            });
            await Look(pair);
            await server.WaitAssertion(() =>
            {
                Assert.That(stasis.Grids[doomed.Grid].State, Is.EqualTo(WGGridStasisState.Pausing), "Precondition: the grid is going into stasis.");
                entMan.DeleteEntity(doomed.Grid);
                server.CfgMan.SetCVar(WGStasisCVars.Budget, 1000f);
            });
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() => Assert.That(stasis.Grids.ContainsKey(doomed.Grid), Is.False, "The deleted grid's record was kept."));

            // Asleep, then woken over several ticks by a viewer inside the wake range but outside the urgent one.
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(WGStasisCVars.WakeRange, stasis.UrgentRange + 100f);
                wreck = BuildWreck(pair, map.MapId, origin);
            });
            await Look(pair);
            Assert.That(stasis.IsInStasis(wreck.Grid), Is.True, "Precondition: the wreck sleeps.");
            await server.WaitPost(() => server.CfgMan.SetCVar(WGStasisCVars.Budget, 0f));
            await Move(pair, viewer, map.MapUid, At(origin, stasis.UrgentRange + 50f));
            await Look(pair, 1);
            await server.WaitAssertion(() =>
            {
                Assert.That(stasis.Grids[wreck.Grid].State, Is.EqualTo(WGGridStasisState.Waking), "Precondition: the wreck is waking bit by bit.");
                maps.SetPaused(map.MapId, true);
                server.CfgMan.SetCVar(WGStasisCVars.Budget, 1000f);
            });
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                Assert.That(Paused(entMan, wreck.Item), Is.True, "Waking unpaused an item on a paused map.");
                Assert.That(Paused(entMan, wreck.Mouse), Is.True, "Waking unpaused a mouse on a paused map.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                maps.SetPaused(map.MapId, false);
                server.CfgMan.SetCVar(WGStasisCVars.WakeRange, wakeRange);
                server.CfgMan.SetCVar(WGStasisCVars.Budget, 2f);
                server.CfgMan.SetCVar(WGStasisCVars.Enabled, false);
            });
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

    /// <summary>A square hull with an anchored wall, a loose item and a mouse on it; call on the server thread.</summary>
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
        var wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid.Owner, new Vector2(4.5f, 4.5f)));
        var item = entMan.SpawnEntity("SheetSteel1", new EntityCoordinates(grid.Owner, new Vector2(2.5f, 2.5f)));
        var mouse = entMan.SpawnEntity("MobMouse", new EntityCoordinates(grid.Owner, new Vector2(3.5f, 2.5f)));
        return new Wreck(grid.Owner, wall, item, mouse);
    }

    /// <summary>Docks two hulls through a port on each.</summary>
    private static void Dock(IEntityManager entMan, EntityUid a, EntityUid b)
    {
        var portA = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(a, new Vector2(4.5f, 0.5f)));
        var portB = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(b, new Vector2(0.5f, 0.5f)));
        entMan.System<DockingSystem>().Dock((portA, entMan.GetComponent<DockingComponent>(portA)),
            (portB, entMan.GetComponent<DockingComponent>(portB)));
    }

    /// <summary>A spot level with a wreck's middle, <paramref name="distance"/> metres off its right edge.</summary>
    private static Vector2 At(Vector2 origin, float distance)
    {
        return origin + new Vector2(Size + distance, Size / 2f);
    }

    private static async Task Move(TestPair pair, EntityUid viewer, EntityUid mapUid, Vector2 position)
    {
        await pair.Server.WaitPost(() =>
            pair.Server.System<SharedTransformSystem>().SetCoordinates(viewer, new EntityCoordinates(mapUid, position)));
    }

    private static bool Paused(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityPaused;
    }
}
