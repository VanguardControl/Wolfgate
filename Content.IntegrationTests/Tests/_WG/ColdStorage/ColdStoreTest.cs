#nullable enable
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Storage.Components;
using Content.Shared.NodeContainer;
using Content.Shared.Atmos;
using Content.Shared._Crescent.Hardpoints;
using Content.Server.NodeContainer.Nodes;
using Content.Server.NodeContainer.NodeGroups;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._Mono.Radar;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.ColdStorage;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Shipyard;
using Content.Server.Power.EntitySystems;
using Content.Shared._Mono.CCVar;
using Content.Shared._WG.Explosion;
using Content.Shared.CCVar;
using Content.Shared.DeviceLinking;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Power.Components;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.ColdStorage;

/// <summary>
/// An ownerless wreck is saved and deleted, leaving one stand-in, and comes back whole where it was when a viewer, a
/// grid or an arrival comes near, or storing is turned off; what can't be stored is refused and a failed save or load
/// loses nothing.
/// </summary>
[TestFixture]
[TestOf(typeof(WGColdStoreSystem))]
public sealed class ColdStoreTest
{
    private const string WreckMap = "/Maps/Salvage/medium-ruined-emergency-shuttle.yml";
    private const int Size = 6;

    /// <summary>A real salvage wreck keeps its place, its entities, a drained cell, a crate's contents and an open door.</summary>
    [Test]
    public async Task ARealWreckComesBackAsItWas()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var origin = new Vector2(0f, 3000f);
        var rotation = new Angle(0.7);
        EntityUid grid = default, door = default;
        Vector2 doorAt = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(map.MapId, new ResPath(WreckMap), out var loaded,
                offset: origin, rot: rotation), Is.True);
            grid = loaded!.Value.Owner;
            entMan.System<MetaDataSystem>().SetEntityName(grid, "Cold store wreck");
        });
        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            // What a wreck spawned on its own and can't be stored: the test is about what can.
            foreach (var uid in Tree(entMan, grid))
            {
                if (!entMan.Deleted(uid) && entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype is { MapSavable: false })
                    entMan.DeleteEntity(uid);
            }

            var spot = new EntityCoordinates(grid, entMan.GetComponent<TransformComponent>(
                Tree(entMan, grid).First(u => u != grid && entMan.GetComponent<TransformComponent>(u).Anchored)).LocalPosition);
            var cell = entMan.SpawnEntity("PowerCellHigh", spot);
            var battery = entMan.GetComponent<BatteryComponent>(cell);
            entMan.System<BatterySystem>().SetCharge(cell, battery.MaxCharge * 0.37f, battery);

            var crate = entMan.SpawnEntity("CrateGenericSteel", spot);
            var crowbar = entMan.SpawnEntity("Crowbar", spot);
            Assert.That(entMan.System<SharedEntityStorageSystem>().Insert(crowbar, crate), Is.True);

            // A storage with two items: each needs its own key in the saved grid layout.
            var box = entMan.SpawnEntity("BoxCardboard", spot);
            for (var i = 0; i < 2; i++)
                Assert.That(entMan.System<SharedStorageSystem>().Insert(box, entMan.SpawnEntity("Pen", spot), out _, playSound: false), Is.True);

            door = Tree(entMan, grid).First(u => entMan.TryGetComponent<DoorComponent>(u, out var d) && d.State == DoorState.Closed
                && !(entMan.TryGetComponent<DoorBoltComponent>(u, out var bolts) && bolts.BoltsDown));
            doorAt = entMan.GetComponent<TransformComponent>(door).LocalPosition;
            entMan.System<SharedDoorSystem>().StartOpening(door);
        });
        await RunUntil(pair, () => entMan.GetComponent<DoorComponent>(door).State == DoorState.Open, 300);

        // Sounds the door made are going anyway and aren't saved: only what saves is counted.
        var before = new Dictionary<string, int>();
        var oldEntities = new List<EntityUid>();
        var savable = 0;
        Vector2 position = default;
        var turned = Angle.Zero;
        await server.WaitPost(() =>
        {
            oldEntities.AddRange(Tree(entMan, grid));
            foreach (var uid in oldEntities.Where(u => Savable(entMan, u)))
            {
                Bump(before, Proto(entMan, uid));
                savable++;
            }

            var xform = entMan.GetComponent<TransformComponent>(grid);
            position = xform.LocalPosition;
            turned = xform.LocalRotation;
        });

        await Enable(pair);
        try
        {
            var standIn = await Store(pair, grid);
            await server.WaitAssertion(() =>
            {
                Assert.That(oldEntities.All(entMan.Deleted), Is.True, "Entities of the stored grid were left behind.");
                Assert.That(entMan.Count<WGStoredGridComponent>(), Is.EqualTo(1), "Not exactly one stand-in.");
                Assert.That(entMan.HasComponent<RadarBlipComponent>(standIn), Is.True, "The stand-in shows nothing on radar.");
                Assert.That(entMan.HasComponent<MapGridComponent>(standIn), Is.False);
                Assert.That(cold.StoredEntities, Is.GreaterThanOrEqualTo(savable));
                Assert.That(entMan.GetComponent<TransformComponent>(standIn).ParentUid, Is.EqualTo(map.MapUid));
            });

            await server.WaitRunTicks(30);
            var restored = await Restore(pair, standIn, "Cold store wreck");
            Action sameAsBefore = () =>
            {
                var xform = entMan.GetComponent<TransformComponent>(restored);
                Assert.That(xform.ParentUid, Is.EqualTo(map.MapUid));
                Assert.That((xform.LocalPosition - position).Length(), Is.LessThan(0.01f), "The wreck came back elsewhere.");
                Assert.That(Math.Abs((xform.LocalRotation - turned).Reduced().Theta), Is.LessThan(1e-4), "The wreck came back turned.");
                Assert.That(entMan.Deleted(standIn), Is.True, "The stand-in outlived the restore.");
                Assert.That(cold.Stored, Is.Empty);

                var after = new Dictionary<string, int>();
                var tree = Tree(entMan, restored);
                foreach (var uid in tree)
                    Bump(after, Proto(entMan, uid));
                var diff = before.Keys.Union(after.Keys)
                    .Where(k => before.GetValueOrDefault(k) != after.GetValueOrDefault(k))
                    .Select(k => $"{k} {before.GetValueOrDefault(k)}->{after.GetValueOrDefault(k)}").ToList();
                TestContext.Out.WriteLine($"Savable entities before {savable}, after {tree.Count}; differences: {string.Join(", ", diff)}");
                Assert.That(diff, Is.Empty, "Prototypes came back in other numbers.");
                Assert.That(tree.Count, Is.EqualTo(savable), "The entity count changed.");

                Assert.That(tree.Any(u => Proto(entMan, u) == "PowerCellHigh" && entMan.TryGetComponent<BatteryComponent>(u, out var b)
                    && Math.Abs(b.CurrentCharge / b.MaxCharge - 0.37f) < 0.01f), Is.True, "The drained cell came back charged.");
                var crate = tree.Single(u => Proto(entMan, u) == "CrateGenericSteel");
                Assert.That(Children(entMan, crate).Any(u => Proto(entMan, u) == "Crowbar"), Is.True, "The crowbar left the crate.");
                var box = tree.Single(u => Proto(entMan, u) == "BoxCardboard");
                var pens = Children(entMan, box).Where(u => Proto(entMan, u) == "Pen").ToList();
                Assert.That(pens, Has.Count.EqualTo(2), "The pens left the box.");
                Assert.That(pens.All(entMan.GetComponent<StorageComponent>(box).StoredItems.ContainsKey), Is.True,
                    "The box lost where its pens sit.");
                var restoredDoor = tree.Single(u => entMan.HasComponent<DoorComponent>(u)
                    && (entMan.GetComponent<TransformComponent>(u).LocalPosition - doorAt).Length() < 0.01f);
                Assert.That(entMan.GetComponent<DoorComponent>(restoredDoor).State, Is.EqualTo(DoorState.Open), "The open door came back shut.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(restoredDoor).EntityPaused, Is.False, "The wreck came back paused.");

                TestContext.Out.WriteLine($"Store: {cold.LastStore}; restore: {cold.LastRestore}");
            };
            await server.WaitAssertion(sameAsBefore);

            // Again, warm: the first save and load of a process pay for the serializer's start-up.
            await server.WaitRunTicks(30);
            var again = await Store(pair, restored);
            restored = await Restore(pair, again, "Cold store wreck");
            await server.WaitPost(() => TestContext.Out.WriteLine($"Second store: {cold.LastStore}; restore: {cold.LastRestore}"));

            // And spread over ticks: parsed off the main thread, then built a slice a tick on a map of its own.
            await server.WaitRunTicks(30);
            standIn = await Store(pair, restored);
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(WGColdStoreCVars.RestoreBudgetMs, 0.5f);
                Assert.That(cold.QueueRestoreAll(), Is.EqualTo(1));
            });
            await RunUntil(pair, () => cold.Stored.Count == 0 && !cold.Restoring);
            await server.WaitPost(() => restored = FindGrid(entMan, "Cold store wreck")!.Value);
            await server.WaitAssertion(sameAsBefore);
            await server.WaitAssertion(() =>
            {
                Assert.That(cold.LastRestoreTicks, Is.GreaterThan(3), "The load was not spread over ticks.");
                TestContext.Out.WriteLine($"Staged restore over {cold.LastRestoreTicks} ticks.");
            });
        }
        finally
        {
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A viewer outside PVS range, another grid and an announced arrival each bring a stored wreck back.</summary>
    [Test]
    public async Task ViewersGridsAndArrivalsRestore()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        var stasis = server.System<WGGridStasisSystem>();
        var map = await pair.CreateTestMap();
        var viewer = await PlanetFixture.AttachViewer(pair, map.MapUid, new Vector2(-20000f, -20000f));
        var watched = new Vector2(0f, 3000f);
        var approached = new Vector2(3000f, 3000f);
        var arrived = new Vector2(6000f, 3000f);
        EntityUid a = default, b = default, c = default;

        await Enable(pair);
        await server.WaitPost(() =>
        {
            a = BuildWreck(pair, map.MapId, watched, "Watched wreck");
            b = BuildWreck(pair, map.MapId, approached, "Approached wreck");
            c = BuildWreck(pair, map.MapId, arrived, "Arrival wreck");
        });

        try
        {
            var standA = await Store(pair, a);
            var standB = await Store(pair, b);
            var standC = await Store(pair, c);
            var range = server.CfgMan.GetCVar(WGColdStoreCVars.RestoreRange);
            var pvs = server.CfgMan.GetCVar(CVars.NetMaxUpdateRange);
            Assert.That(range - 10f, Is.GreaterThan(pvs), "Precondition: the viewer stands outside PVS range.");

            // A viewer 590 m off the bounds: well past what PVS would show.
            await server.WaitPost(() => server.System<SharedTransformSystem>().SetCoordinates(viewer,
                new EntityCoordinates(map.MapUid, watched + new Vector2(Size + range - 10f, Size / 2f))));
            await RunUntil(pair, () => entMan.Deleted(standA), 60);
            Assert.That(entMan.Deleted(standB) || entMan.Deleted(standC), Is.False, "A far wreck was restored too.");

            // Another grid within half the restore range.
            await server.WaitPost(() => BuildWreck(pair, map.MapId, approached + new Vector2(Size + range / 2f - 40f, 0f), "Passer-by"));
            await RunUntil(pair, () => entMan.Deleted(standB), 90);
            Assert.That(entMan.Deleted(standC), Is.False);

            // A jump announced to exit beside it.
            await server.WaitPost(() => stasis.NoteArrival(new MapCoordinates(arrived + new Vector2(-200f, 0f), map.MapId), TimeSpan.FromSeconds(10)));
            await RunUntil(pair, () => entMan.Deleted(standC), 60);

            await server.WaitAssertion(() =>
            {
                Assert.That(cold.Stored, Is.Empty);
                Assert.That(cold.Restores, Is.EqualTo(3));
                foreach (var name in new[] { "Watched wreck", "Approached wreck", "Arrival wreck" })
                    Assert.That(FindGrid(entMan, name), Is.Not.Null, $"{name} did not come back.");
            });
        }
        finally
        {
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A mob aboard, a drifting hull and a small one are refused, and say why.</summary>
    [Test]
    public async Task UnfitGridsAreRefused()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        var map = await pair.CreateTestMap();

        await Enable(pair);
        try
        {
            await server.WaitAssertion(() =>
            {
                var mob = BuildWreck(pair, map.MapId, new Vector2(0f, 3000f), "Mob wreck");
                entMan.SpawnEntity("MobMouse", new EntityCoordinates(mob, new Vector2(3.5f, 3.5f)));
                Assert.That(cold.TryStore(mob, true, out var why), Is.False);
                Assert.That(why, Is.EqualTo("wg-coldstore-why-unsaved"));

                var drifting = BuildWreck(pair, map.MapId, new Vector2(3000f, 3000f), "Drifting wreck");
                entMan.System<SharedPhysicsSystem>().SetLinearVelocity(drifting, new Vector2(0.5f, 0f));
                Assert.That(cold.TryStore(drifting, true, out why), Is.False);
                Assert.That(why, Is.EqualTo("wg-coldstore-why-moving"));

                var small = BuildWreck(pair, map.MapId, new Vector2(6000f, 3000f), "Small wreck");
                server.CfgMan.SetCVar(WGColdStoreCVars.MinEntities, 1000);
                Assert.That(cold.TryStore(small, true, out why), Is.False);
                Assert.That(why, Is.EqualTo("wg-coldstore-why-small"));

                Assert.That(cold.Stored, Is.Empty);
                Assert.That(cold.Storing, Is.False);

                // Off, nothing is stored, even by hand.
                var fine = BuildWreck(pair, map.MapId, new Vector2(9000f, 3000f), "Fine wreck");
                server.CfgMan.SetCVar(WGColdStoreCVars.Enabled, false);
                Assert.That(cold.TryStore(fine, true, out why), Is.False);
                Assert.That(why, Is.EqualTo("wg-coldstore-why-disabled"));
                Assert.That(cold.Storing, Is.False);
            });
        }
        finally
        {
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A save that throws puts the wreck back as it was and keeps it from being tried again.</summary>
    [Test]
    public async Task AnAbortedSaveLeavesTheWreck()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        var map = await pair.CreateTestMap();
        EntityUid grid = default;
        var count = 0;
        var origin = new Vector2(0f, 3000f);

        await Enable(pair);
        try
        {
            await server.WaitPost(() =>
            {
                grid = BuildWreck(pair, map.MapId, origin, "Aborted wreck");
                server.System<SharedTransformSystem>().SetLocalRotation(grid, new Angle(1.1));
                count = Tree(entMan, grid).Count;
                server.System<WGHullRetireSystem>().FailNextKeptCopy = true;
                Assert.That(cold.TryStore(grid, true, out var why), Is.True, why);
            });
            await RunUntil(pair, () => !cold.Storing);

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.Deleted(grid), Is.False, "The wreck was deleted.");
                var xform = entMan.GetComponent<TransformComponent>(grid);
                Assert.That(xform.ParentUid, Is.EqualTo(map.MapUid), "The wreck was left parked.");
                Assert.That((xform.LocalPosition - origin).Length(), Is.LessThan(0.01f));
                Assert.That(Math.Abs((xform.LocalRotation - new Angle(1.1)).Reduced().Theta), Is.LessThan(1e-4));
                Assert.That(Tree(entMan, grid).Count, Is.EqualTo(count), "The wreck lost entities.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(grid).EntityPaused, Is.False);
                Assert.That(entMan.Count<WGStoredGridComponent>(), Is.Zero, "A stand-in was left.");
                Assert.That(cold.Aborts, Is.EqualTo(1));
                Assert.That(cold.Ineligible(grid, true), Is.EqualTo("wg-coldstore-why-aborted"));
                Assert.That(server.System<WGGridStasisSystem>().IsPinned(grid), Is.False, "The park pin was kept.");
            });
        }
        finally
        {
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A store that aborts after its copy began leaves the wreck as it was: a link to a device on another grid, which
    /// the copy won't hold, isn't pruned from the live hull.
    /// </summary>
    [Test]
    public async Task AnAbortedStoreKeepsLinksOffTheWreck()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var origin = new Vector2(0f, 3000f);
        EntityUid grid = default, button = default, door = default;

        await Enable(pair);
        try
        {
            await server.WaitPost(() =>
            {
                grid = BuildWreck(pair, map.MapId, origin, "Linked wreck");
                button = entMan.SpawnEntity("SignalButton", new EntityCoordinates(grid, new Vector2(3.5f, 1.5f)));

                // Linked while in range, then taken far off.
                var maps = server.System<SharedMapSystem>();
                var other = maps.CreateGridEntity(map.MapId);
                xforms.SetLocalPosition(other.Owner, origin + new Vector2(Size + 1f, 0f));
                maps.SetTile(other.Owner, other.Comp, Vector2i.Zero, map.Tile.Tile);
                door = entMan.SpawnEntity("Airlock", new EntityCoordinates(other.Owner, new Vector2(0.5f, 0.5f)));
                entMan.System<SharedDeviceLinkSystem>().LinkDefaults(null, button, door);
                var ports = entMan.GetComponent<DeviceLinkSourceComponent>(button).LinkedPorts;
                Assert.That(ports.ContainsKey(door), Is.True, "Precondition: the button is linked to the door.");
                xforms.SetLocalPosition(other.Owner, origin + new Vector2(5000f, 0f));

                server.System<WGHullRetireSystem>().FailNextKeptCopy = true;
                Assert.That(cold.TryStore(grid, true, out var why), Is.True, why);
            });
            await RunUntil(pair, () => !cold.Storing);

            await server.WaitAssertion(() =>
            {
                Assert.That(cold.Aborts, Is.EqualTo(1), "Precondition: the store aborted.");
                Assert.That(entMan.Deleted(grid), Is.False);
                var ports = entMan.GetComponent<DeviceLinkSourceComponent>(button).LinkedPorts;
                Assert.That(ports.ContainsKey(door), Is.True, "The aborted store pruned the wreck's link to another grid.");
                var sources = entMan.GetComponent<DeviceLinkSinkComponent>(door).LinkedSources;
                Assert.That(sources, Does.Contain(button));
            });
        }
        finally
        {
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A store that aborts after the wreck's map was deleted deletes the parked wreck instead of leaving it parked.</summary>
    [Test]
    public async Task AnAbortWithItsMapGoneDeletesTheWreck()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        await pair.CreateTestMap();
        EntityUid grid = default;

        await Enable(pair);
        try
        {
            await server.WaitPost(() =>
            {
                var doomedMap = server.System<SharedMapSystem>().CreateMap(out var mapId);
                grid = BuildWreck(pair, mapId, new Vector2(0f, 3000f), "Orphaned wreck");
                server.System<WGHullRetireSystem>().FailNextKeptCopy = true;
                Assert.That(cold.TryStore(grid, true, out var why), Is.True, why);
                Assert.That(entMan.GetComponent<TransformComponent>(grid).MapUid, Is.Not.EqualTo(doomedMap), "Precondition: the wreck is parked.");
                entMan.DeleteEntity(doomedMap);
            });
            await RunUntil(pair, () => !cold.Storing);
            await RunUntil(pair, () => entMan.Deleted(grid), 120);
            await server.WaitAssertion(() =>
            {
                Assert.That(cold.Aborts, Is.EqualTo(1));
                Assert.That(server.System<WGGridStasisSystem>().IsPinned(grid), Is.False, "The park pin outlived the wreck.");
            });
        }
        finally
        {
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A claim on the wreck held by something paused, such as a locker on a grid in stasis, still keeps it.</summary>
    [Test]
    public async Task APausedClaimHolderKeepsTheWreck()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        var map = await pair.CreateTestMap();

        await Enable(pair);
        try
        {
            await server.WaitAssertion(() =>
            {
                var grid = BuildWreck(pair, map.MapId, new Vector2(0f, 3000f), "Claimed wreck");
                var holder = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(-5000f, 3000f), map.MapId));
                entMan.AddComponent<WGSalvageClaimComponent>(holder).Ship = grid;
                entMan.System<MetaDataSystem>().SetEntityPaused(holder, true);

                Assert.That(cold.TryStore(grid, true, out var why), Is.False, "A wreck with a paused claim was stored.");
                Assert.That(why, Is.EqualTo("wg-coldstore-why-claim"));
                entMan.DeleteEntity(holder);
            });
        }
        finally
        {
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A blast that brings back a wreck whose load fails keeps it queued: other stand-ins aren't held up while it waits,
    /// and it is tried again when due.
    /// </summary>
    [Test]
    public async Task AFailedRestoreFromABlastIsRetried()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        var map = await pair.CreateTestMap();
        var originA = new Vector2(0f, 3000f);
        var originB = new Vector2(3000f, 3000f);
        EntityUid a = default, b = default;

        await Enable(pair);
        var failureLevel = pair.ServerLogHandler.FailureLevel;
        try
        {
            await server.WaitPost(() =>
            {
                a = BuildWreck(pair, map.MapId, originA, "Retried wreck");
                b = BuildWreck(pair, map.MapId, originB, "Waiting wreck");
            });
            var standA = await Store(pair, a);
            var standB = await Store(pair, b);
            WGStoredGrid record = default!;
            byte[] good = default!;

            // The failed load logs errors, which is the point.
            pair.ServerLogHandler.FailureLevel = LogLevel.Fatal;
            await server.WaitPost(() =>
            {
                record = cold.Stored[standA];
                Assert.That(cold.Backend.TryGet(record.Key, out good), Is.True);
                cold.Backend.Put(record.Key, Gzip("this: [is not a grid"), new WGColdStoreMeta(record.Name, record.Entities, record.StoredAt));
                Blast(entMan, map.MapId, originA);
            });
            await RunUntil(pair, () => record.Failures == 1, 30);
            Assert.That(entMan.Deleted(standA), Is.False, "Precondition: the load failed.");

            await server.WaitPost(() => Blast(entMan, map.MapId, originB));
            await RunUntil(pair, () => entMan.Deleted(standB), 30);
            Assert.That(entMan.Deleted(standA), Is.False);

            await server.WaitPost(() =>
            {
                cold.Backend.Put(record.Key, good, new WGColdStoreMeta(record.Name, record.Entities, record.StoredAt));
                record.NextRetry = TimeSpan.Zero;
            });
            await RunUntil(pair, () => entMan.Deleted(standA), 30);

            await server.WaitAssertion(() =>
            {
                Assert.That(FindGrid(entMan, "Retried wreck"), Is.Not.Null);
                Assert.That(FindGrid(entMan, "Waiting wreck"), Is.Not.Null);
                Assert.That(cold.Failures, Is.EqualTo(1));
                Assert.That(cold.Restores, Is.EqualTo(2));
            });
        }
        finally
        {
            pair.ServerLogHandler.FailureLevel = failureLevel;
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A wreck that would come back on top of another grid however far it is moved stays stored, bytes and all.</summary>
    [Test]
    public async Task ARestoreOntoAGridKeepsTheStandIn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        var map = await pair.CreateTestMap();
        var origin = new Vector2(0f, 3000f);
        EntityUid grid = default;

        await Enable(pair);
        var failureLevel = pair.ServerLogHandler.FailureLevel;
        try
        {
            await server.WaitPost(() => grid = BuildWreck(pair, map.MapId, origin, "Buried wreck"));
            var standIn = await Store(pair, grid);
            WGStoredGrid record = default!;

            // A strip of hull 600 m long right through where the wreck was: no eight hull-widths clear it.
            pair.ServerLogHandler.FailureLevel = LogLevel.Fatal;
            await server.WaitPost(() =>
            {
                record = cold.Stored[standIn];
                var maps = server.System<SharedMapSystem>();
                var strip = maps.CreateGridEntity(map.MapId);
                server.System<SharedTransformSystem>().SetLocalPosition(strip.Owner, origin);
                var tiles = new List<(Vector2i, Tile)>();
                for (var x = 0; x < Size; x++)
                for (var y = -300; y < 300; y++)
                    tiles.Add((new Vector2i(x, y), map.Tile.Tile));
                maps.SetTiles(strip.Owner, strip.Comp, tiles);
            });
            await RunUntil(pair, () => record.Failures >= 1, 120);

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.Deleted(standIn), Is.False, "The stand-in went with a restore that couldn't be placed.");
                Assert.That(cold.Stored.ContainsKey(standIn), Is.True);
                Assert.That(cold.Backend.TryGet(record.Key, out _), Is.True, "The bytes were dropped.");
                Assert.That(FindGrid(entMan, "Buried wreck"), Is.Null, "The overlapping copy was left in the world.");
                Assert.That(record.LastError, Does.Contain("overlap"));
            });
        }
        finally
        {
            pair.ServerLogHandler.FailureLevel = failureLevel;
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A load that fails keeps the stand-in and the bytes and reports it.</summary>
    [Test]
    public async Task AFailedLoadKeepsTheStandIn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        var map = await pair.CreateTestMap();
        EntityUid grid = default;

        await Enable(pair);
        try
        {
            await server.WaitPost(() => grid = BuildWreck(pair, map.MapId, new Vector2(0f, 3000f), "Corrupt wreck"));
            var standIn = await Store(pair, grid);
            await server.WaitAssertion(() =>
            {
                var record = cold.Stored[standIn];
                cold.Backend.Put(record.Key, Gzip("this: [is not a grid"), new WGColdStoreMeta(record.Name, record.Entities, record.StoredAt));

                // The failed load logs an error, which is the point.
                var failureLevel = pair.ServerLogHandler.FailureLevel;
                pair.ServerLogHandler.FailureLevel = LogLevel.Fatal;
                bool restored;
                try
                {
                    restored = cold.RestoreNow(standIn);
                }
                finally
                {
                    pair.ServerLogHandler.FailureLevel = failureLevel;
                }

                Assert.That(restored, Is.False);
                Assert.That(entMan.Deleted(standIn), Is.False, "The stand-in went with the failed load.");
                Assert.That(cold.Stored.ContainsKey(standIn), Is.True);
                Assert.That(record.State, Is.EqualTo(WGStoredState.Failed));
                Assert.That(record.Failures, Is.EqualTo(1));
                Assert.That(record.LastError, Is.Not.Null);
                Assert.That(cold.Backend.TryGet(record.Key, out _), Is.True, "The bytes were dropped.");
                Assert.That(cold.Failures, Is.EqualTo(1));
            });
        }
        finally
        {
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A wreck left long enough in stasis is stored on its own, and turning storing off brings everything back.</summary>
    [Test]
    public async Task IdleWrecksAreStoredAndTurningOffRestoresThem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        var map = await pair.CreateTestMap();

        await Enable(pair);
        try
        {
            await server.WaitPost(() =>
            {
                BuildWreck(pair, map.MapId, new Vector2(0f, 3000f), "Idle wreck one");
                BuildWreck(pair, map.MapId, new Vector2(3000f, 3000f), "Idle wreck two");
                server.CfgMan.SetCVar(WGColdStoreCVars.Idle, 1f);
            });

            await RunUntil(pair, () => cold.Stored.Count == 2 && !cold.Storing, 30 * 30);
            await server.WaitPost(() => server.CfgMan.SetCVar(WGColdStoreCVars.Enabled, false));
            await RunUntil(pair, () => cold.Stored.Count == 0, 60);

            await server.WaitAssertion(() =>
            {
                Assert.That(cold.Restores, Is.EqualTo(2));
                Assert.That(entMan.Count<WGStoredGridComponent>(), Is.Zero);
                Assert.That(FindGrid(entMan, "Idle wreck one"), Is.Not.Null);
                Assert.That(FindGrid(entMan, "Idle wreck two"), Is.Not.Null);
            });
        }
        finally
        {
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A stored wreck grid cleanup would have deleted runs out the same clock and goes, bytes and all.</summary>
    [Test]
    public async Task AStoredCleanupCandidateExpires()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cold = server.System<WGColdStoreSystem>();
        var map = await pair.CreateTestMap();
        EntityUid grid = default;
        var duration = 0f;

        await Enable(pair);
        try
        {
            await server.WaitPost(() => grid = BuildWreck(pair, map.MapId, new Vector2(0f, 3000f), "Junk wreck"));
            var standIn = await Store(pair, grid);
            string key = default!;
            await server.WaitPost(() =>
            {
                var record = cold.Stored[standIn];
                Assert.That(record.CleanupCandidate, Is.True, "Precondition: a cheap unlabelled wreck is a cleanup candidate.");
                key = record.Key;
                duration = server.CfgMan.GetCVar(MonoCVars.GridCleanupDuration);
                server.CfgMan.SetCVar(MonoCVars.GridCleanupDuration, 1f);
            });

            await RunUntil(pair, () => entMan.Deleted(standIn), 120);
            await server.WaitAssertion(() =>
            {
                Assert.That(cold.Stored, Is.Empty);
                Assert.That(cold.Expired, Is.EqualTo(1));
                Assert.That(cold.Backend.TryGet(key, out _), Is.False, "The bytes outlived the stand-in.");
                Assert.That(FindGrid(entMan, "Junk wreck"), Is.Null, "The wreck was restored instead.");
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(MonoCVars.GridCleanupDuration, duration));
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The resale strip step reserves every id first, so a box holding two or more items isn't written with two
    /// "invalid" keys and stripped of its storage.
    /// </summary>
    [Test]
    public async Task StripKeepsAStorageWithSeveralItems()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
            var at = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var box = entMan.SpawnEntity("BoxCardboard", at);
            var storage = entMan.System<SharedStorageSystem>();
            var pens = new List<EntityUid>();
            for (var i = 0; i < 2; i++)
            {
                var item = entMan.SpawnEntity("Pen", at);
                storage.Insert(box, item, out _, playSound: false);
                pens.Add(item);
            }

            Assert.That(pens.All(entMan.GetComponent<StorageComponent>(box).StoredItems.ContainsKey), Is.True, "Precondition: both pens in the box.");

            var options = SerializationOptions.Default with
            {
                MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
                LogAutoInclude = null,
                Category = FileCategory.Grid,
                ErrorOnOrphan = false,
            };
            entMan.System<ShipyardSystem>().WGStripUnsavable(grid.Owner, options);
            Assert.That(entMan.HasComponent<StorageComponent>(box), Is.True, "The strip step took a healthy box's storage.");
            var stored = entMan.GetComponent<StorageComponent>(box).StoredItems;
            Assert.That(pens.All(stored.ContainsKey), Is.True, "The strip step dropped a pen from the box's stored items.");
            entMan.DeleteEntity(grid.Owner);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// State a save does not write comes back: gas in a pipe, which crate an item is in, a gun's modified fire rate
    /// and a ship gun's hardpoint.
    /// </summary>
    [Test]
    public async Task UnsavedStateComesBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid grid = default, pipe = default;

        await server.WaitPost(() =>
        {
            grid = BuildWreck(pair, map.MapId, new Vector2(0f, 6000f), "Kept wreck");
            pipe = entMan.SpawnEntity("GasPipeStraight", new EntityCoordinates(grid, new Vector2(2.5f, 4.5f)));
            var crate = Tree(entMan, grid).Single(u => Proto(entMan, u) == "CrateGenericSteel");
            var crowbar = entMan.SpawnEntity("Crowbar", new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
            Assert.That(entMan.System<SharedEntityStorageSystem>().Insert(crowbar, crate), Is.True);
            entMan.SpawnEntity("WeaponPistolMk58", new EntityCoordinates(grid, new Vector2(3.5f, 1.5f)));
            entMan.SpawnEntity("HardpointDebugLight", new EntityCoordinates(grid, new Vector2(4.5f, 4.5f)));
            entMan.SpawnEntity("WGIonDisruptor", new EntityCoordinates(grid, new Vector2(4.5f, 4.5f)));
        });
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            var node = entMan.GetComponent<NodeContainerComponent>(pipe).Nodes.Values.OfType<PipeNode>().First();
            Assert.That(node.NodeGroup, Is.InstanceOf<IPipeNet>(), "Precondition: the pipe has a net.");
            ((IPipeNet) node.NodeGroup!).Air.AdjustMoles(Gas.Plasma, 30f);

            var gun = Tree(entMan, grid).Single(u => Proto(entMan, u) == "WGIonDisruptor");
            Assert.That(entMan.GetComponent<HardpointAnchorableOnlyComponent>(gun).anchoredTo, Is.Not.Null,
                "Precondition: the gun is on its hardpoint.");
        });

        await Enable(pair);
        try
        {
            var standIn = await Store(pair, grid);
            await server.WaitRunTicks(30);
            var restored = await Restore(pair, standIn, "Kept wreck");
            await server.WaitRunTicks(30);
            await server.WaitAssertion(() => Assert.Multiple(() =>
            {
                var tree = Tree(entMan, restored);
                var newPipe = tree.Single(u => Proto(entMan, u) == "GasPipeStraight");
                var node = entMan.GetComponent<NodeContainerComponent>(newPipe).Nodes.Values.OfType<PipeNode>().First();
                Assert.That(node.NodeGroup, Is.InstanceOf<IPipeNet>());
                Assert.That(((IPipeNet) node.NodeGroup!).Air.GetMoles(Gas.Plasma), Is.EqualTo(30f).Within(0.01f), "The pipe came back empty.");
                Assert.That(entMan.Count<WGKeptPipeGasComponent>(), Is.Zero, "A pipe still carries its kept gas.");

                var crate = tree.Single(u => Proto(entMan, u) == "CrateGenericSteel");
                var crowbar = tree.Single(u => Proto(entMan, u) == "Crowbar");
                Assert.That(entMan.TryGetComponent<InsideEntityStorageComponent>(crowbar, out var inside) && inside.Storage == crate,
                    Is.True, "The crowbar does not know which crate it is in.");

                var pistol = tree.Single(u => Proto(entMan, u) == "WeaponPistolMk58");
                Assert.That(entMan.GetComponent<GunComponent>(pistol).FireRateModified, Is.GreaterThan(0f), "The pistol has no fire rate.");

                var gun = tree.Single(u => Proto(entMan, u) == "WGIonDisruptor");
                var hardpoint = tree.Single(u => Proto(entMan, u) == "HardpointDebugLight");
                Assert.That(entMan.GetComponent<HardpointAnchorableOnlyComponent>(gun).anchoredTo, Is.EqualTo(hardpoint), "The gun is off its hardpoint.");
                Assert.That(entMan.GetComponent<HardpointComponent>(hardpoint).anchoring, Is.EqualTo(gun));
            }));
        }
        finally
        {
            await Disable(pair);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Turns stasis and storing on, with stasis immediate and storing left to the tests.</summary>
    private static async Task Enable(TestPair pair)
    {
        await pair.Server.WaitPost(() =>
        {
            // A pooled server carries the counts of whichever test had it before.
            var cold = pair.Server.System<WGColdStoreSystem>();
            cold.Stores = cold.Restores = cold.Aborts = cold.Failures = cold.Expired = 0;

            var cfg = pair.Server.CfgMan;
            cfg.SetCVar(CCVars.NPCEnabled, false);
            cfg.SetCVar(WGStasisCVars.Idle, 0f);
            cfg.SetCVar(WGStasisCVars.Budget, 1000f);
            cfg.SetCVar(WGStasisCVars.Sweep, 1f);
            cfg.SetCVar(WGStasisCVars.Enabled, true);
            cfg.SetCVar(WGColdStoreCVars.MinEntities, 5);
            cfg.SetCVar(WGColdStoreCVars.Idle, 100000f);
            cfg.SetCVar(WGColdStoreCVars.Enabled, true);
        });
    }

    /// <summary>Turns storing and stasis back off and their cvars back to defaults; whatever is stored comes back.</summary>
    private static async Task Disable(TestPair pair)
    {
        await pair.Server.WaitPost(() =>
        {
            var cfg = pair.Server.CfgMan;
            cfg.SetCVar(WGColdStoreCVars.Enabled, false);
            cfg.SetCVar(WGColdStoreCVars.RestoreBudgetMs, 0f);
            cfg.SetCVar(WGColdStoreCVars.MinEntities, 150);
            cfg.SetCVar(WGColdStoreCVars.Idle, 900f);
            cfg.SetCVar(WGStasisCVars.Enabled, false);
        });
        await pair.Server.WaitRunTicks(10);
    }

    /// <summary>Stores a grid now and waits for it to finish; gives the stand-in.</summary>
    private static async Task<EntityUid> Store(TestPair pair, EntityUid grid)
    {
        var cold = pair.Server.System<WGColdStoreSystem>();
        var name = string.Empty;
        await pair.Server.WaitPost(() =>
        {
            name = pair.Server.EntMan.GetComponent<MetaDataComponent>(grid).EntityName;
            Assert.That(cold.TryStore(grid, true, out var why), Is.True, $"Refused: {why}");
        });

        await RunUntil(pair, () => !cold.Storing);
        var standIn = EntityUid.Invalid;
        await pair.Server.WaitPost(() => standIn = cold.Stored.SingleOrDefault(pair => pair.Value.Name == name).Key);
        Assert.That(standIn.IsValid(), $"{name} was not stored.");
        return standIn;
    }

    /// <summary>Restores a stand-in now and gives the grid that came back.</summary>
    private static async Task<EntityUid> Restore(TestPair pair, EntityUid standIn, string name)
    {
        var cold = pair.Server.System<WGColdStoreSystem>();
        EntityUid? grid = null;
        await pair.Server.WaitPost(() =>
        {
            Assert.That(cold.RestoreNow(standIn), Is.True, "The restore failed.");
            grid = FindGrid(pair.Server.EntMan, name);
        });

        Assert.That(grid, Is.Not.Null);
        return grid!.Value;
    }

    /// <summary>A square hull with a door, a crate, a cell and some sheets on it; call on the server thread.</summary>
    private static EntityUid BuildWreck(TestPair pair, MapId mapId, Vector2 position, string name)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(mapId);
        pair.Server.System<SharedTransformSystem>().SetLocalPosition(grid.Owner, position);
        entMan.System<MetaDataSystem>().SetEntityName(grid.Owner, name);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < Size; x++)
        for (var y = 0; y < Size; y++)
            tiles.Add((new Vector2i(x, y), pair.TestMap!.Tile.Tile));

        maps.SetTiles(grid.Owner, grid.Comp, tiles);
        entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid.Owner, new Vector2(5.5f, 5.5f)));
        entMan.SpawnEntity("CrateGenericSteel", new EntityCoordinates(grid.Owner, new Vector2(1.5f, 1.5f)));
        entMan.SpawnEntity("PowerCellHigh", new EntityCoordinates(grid.Owner, new Vector2(2.5f, 1.5f)));
        for (var i = 0; i < 4; i++)
            entMan.SpawnEntity("SheetSteel1", new EntityCoordinates(grid.Owner, new Vector2(1.5f + i, 3.5f)));

        return grid.Owner;
    }

    /// <summary>Raises a small blast's shockwave over a wreck built at <paramref name="origin"/>.</summary>
    private static void Blast(IEntityManager entMan, MapId mapId, Vector2 origin)
    {
        var blast = new ExplosionShockwaveEvent(new MapCoordinates(origin + new Vector2(Size / 2f, Size / 2f), mapId), 3, null);
        entMan.EventBus.RaiseEvent(EventSource.Local, ref blast);
    }

    private static EntityUid? FindGrid(IEntityManager entMan, string name)
    {
        var query = entMan.EntityQueryEnumerator<MapGridComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityName == name && meta.EntityLifeStage < EntityLifeStage.Terminating)
                return uid;
        }

        return null;
    }

    private static List<EntityUid> Tree(IEntityManager entMan, EntityUid root)
    {
        var list = new List<EntityUid> { root };
        for (var i = 0; i < list.Count; i++)
            list.AddRange(Children(entMan, list[i]));

        return list;
    }

    private static List<EntityUid> Children(IEntityManager entMan, EntityUid uid)
    {
        var list = new List<EntityUid>();
        var children = entMan.GetComponent<TransformComponent>(uid).ChildEnumerator;
        while (children.MoveNext(out var child))
            list.Add(child);

        return list;
    }

    private static bool Savable(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.MapSavable != false;
    }

    private static string Proto(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "(none)";
    }

    private static void Bump(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
        {
            var raw = Encoding.UTF8.GetBytes(text);
            gzip.Write(raw, 0, raw.Length);
        }

        return output.ToArray();
    }

    /// <summary>Runs ticks until the condition, read on the server thread, holds.</summary>
    private static async Task RunUntil(TestPair pair, Func<bool> done, int maxTicks = 1800)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var finished = false;
            await pair.Server.WaitPost(() => finished = done());
            if (finished)
                return;

            await pair.Server.WaitRunTicks(1);
        }

        var last = false;
        await pair.Server.WaitPost(() => last = done());
        Assert.That(last, Is.True, $"It didn't happen within {maxTicks} ticks.");
    }
}
