#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Mapping;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Placement;

namespace Content.IntegrationTests.Tests._WF.Mapping;

/// <summary>
/// Covers the mapper's undo stack: what gets recorded, what comes back, and what is deliberately ignored.
/// </summary>
[TestFixture]
[TestOf(typeof(MappingUndoSystem))]
public sealed class MappingUndoTest
{
    private const string WallProto = "WallSolid";
    private const string TableProto = "Table";
    private const string LockerProto = "LockerSteel";
    private const string ItemProto = "Crowbar";
    private const string TraderProto = "WFTraderFuelTechnician";

    /// <summary>A placement on an uninitialised map undoes to nothing and redoes back.</summary>
    [Test]
    public async Task CreateUndoRedo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var undo = entMan.System<MappingUndoSystem>();
        var user = new NetUserId(Guid.NewGuid());

        MapId mapId = default;
        Entity<MapGridComponent> grid = default;
        var wall = EntityUid.Invalid;
        var recorded = (0, 0);
        var goneAfterUndo = false;
        var backAfterRedo = false;
        var restoredProto = string.Empty;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out mapId, runMapInit: false);
            grid = mapMan.CreateGridEntity(mapId);
            Fill(mapSys, grid);

            wall = entMan.SpawnEntity(WallProto, new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));

            // Exactly what the engine's placement manager raises after it spawns a placed entity.
            var ev = new PlacementEntityEvent(wall,
                entMan.GetComponent<TransformComponent>(wall).Coordinates,
                PlacementEventAction.Create,
                user);
            entMan.EventBus.RaiseEvent(EventSource.Local, ev);

            recorded = undo.GetCounts(user);
        });

        await server.WaitPost(() =>
        {
            undo.Apply(user, 1, redo: false);
            goneAfterUndo = entMan.Deleted(wall);
        });

        await server.WaitPost(() =>
        {
            undo.Apply(user, 1, redo: true);

            var step = undo.GetSteps(user, redo: false).LastOrDefault();
            var restored = step?.Entities[0].Handle.Uid ?? EntityUid.Invalid;
            backAfterRedo = entMan.EntityExists(restored);
            if (backAfterRedo)
                restoredProto = entMan.GetComponent<MetaDataComponent>(restored).EntityPrototype?.ID ?? string.Empty;
        });

        Assert.Multiple(() =>
        {
            Assert.That(recorded.Item1, Is.EqualTo(1), "The placement should have been recorded as one step.");
            Assert.That(goneAfterUndo, Is.True, "Undo should have removed the placed wall.");
            Assert.That(backAfterRedo, Is.True, "Redo should have put the wall back.");
            Assert.That(restoredProto, Is.EqualTo(WallProto));
        });

        await server.WaitPost(() => mapSys.DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }

    /// <summary>An erased table and a locker with something in it come back whole, still pre-mapinit.</summary>
    [Test]
    public async Task EraseRestoresPlaceAndContents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var storage = entMan.System<EntityStorageSystem>();
        var undo = entMan.System<MappingUndoSystem>();
        var user = new NetUserId(Guid.NewGuid());

        MapId mapId = default;
        Entity<MapGridComponent> grid = default;

        var tablePos = Vector2.Zero;
        var tableRot = Angle.Zero;
        var tableAnchored = false;
        var inserted = false;

        var restoredTable = EntityUid.Invalid;
        var restoredLocker = EntityUid.Invalid;
        var newTablePos = Vector2.Zero;
        var newTableRot = Angle.Zero;
        var newTableAnchored = false;
        var contained = -1;
        var containedProto = string.Empty;
        var tableStage = EntityLifeStage.Terminating;
        var lockerStage = EntityLifeStage.Terminating;
        var mapStillPreInit = false;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out mapId, runMapInit: false);
            grid = mapMan.CreateGridEntity(mapId);
            Fill(mapSys, grid);

            var table = entMan.SpawnEntity(TableProto, new EntityCoordinates(grid.Owner, new Vector2(1.5f, 2.5f)));
            var locker = entMan.SpawnEntity(LockerProto, new EntityCoordinates(grid.Owner, new Vector2(2.5f, 2.5f)));
            var item = entMan.SpawnEntity(ItemProto, new EntityCoordinates(grid.Owner, new Vector2(2.5f, 2.5f)));
            inserted = storage.Insert(item, locker);

            var tableXform = entMan.GetComponent<TransformComponent>(table);
            tablePos = tableXform.LocalPosition;
            tableRot = tableXform.LocalRotation;
            tableAnchored = tableXform.Anchored;

            var mapUid = mapSys.GetMap(mapId);
            undo.RecordErased(user, mapUid, (table, tableXform));
            undo.RecordErased(user, mapUid, (locker, entMan.GetComponent<TransformComponent>(locker)));
            entMan.DeleteEntity(table);
            entMan.DeleteEntity(locker);
        });

        await server.WaitPost(() =>
        {
            undo.Apply(user, 1, redo: false);

            var step = undo.GetSteps(user, redo: true).Last();
            restoredTable = step.Entities[0].Handle.Uid;
            restoredLocker = step.Entities[1].Handle.Uid;

            var xform = entMan.GetComponent<TransformComponent>(restoredTable);
            newTablePos = xform.LocalPosition;
            newTableRot = xform.LocalRotation;
            newTableAnchored = xform.Anchored;

            if (containers.TryGetContainer(restoredLocker, SharedEntityStorageSystem.ContainerName, out var container))
            {
                contained = container.ContainedEntities.Count;
                if (contained == 1)
                {
                    containedProto = entMan.GetComponent<MetaDataComponent>(container.ContainedEntities[0])
                        .EntityPrototype?.ID ?? string.Empty;
                }
            }

            tableStage = entMan.GetComponent<MetaDataComponent>(restoredTable).EntityLifeStage;
            lockerStage = entMan.GetComponent<MetaDataComponent>(restoredLocker).EntityLifeStage;
            mapStillPreInit = !entMan.GetComponent<MapComponent>(mapSys.GetMap(mapId)).MapInitialized;
        });

        Assert.Multiple(() =>
        {
            Assert.That(inserted, Is.True, "The item should have gone into the locker before the erase.");
            Assert.That(entMan.EntityExists(restoredTable), Is.True, "The table should have come back.");
            Assert.That(entMan.EntityExists(restoredLocker), Is.True, "The locker should have come back.");
            Assert.That(newTablePos, Is.EqualTo(tablePos), "The table should be back where it was.");
            Assert.That(newTableRot, Is.EqualTo(tableRot), "The table should be facing the way it was.");
            Assert.That(newTableAnchored, Is.EqualTo(tableAnchored), "The table should be anchored the way it was.");
            Assert.That(contained, Is.EqualTo(1), "The locker should hold exactly what it held before.");
            Assert.That(containedProto, Is.EqualTo(ItemProto));
            Assert.That(tableStage, Is.LessThan(EntityLifeStage.MapInitialized), "Restoring must not map-init the table.");
            Assert.That(lockerStage, Is.LessThan(EntityLifeStage.MapInitialized), "Restoring must not map-init the locker.");
            Assert.That(mapStillPreInit, Is.True, "The mapping map must still be uninitialised.");
        });

        await server.WaitPost(() => mapSys.DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }

    /// <summary>A humanoid with a loadout survives the round trip, gear containers and all.</summary>
    [Test]
    public async Task MobRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var undo = entMan.System<MappingUndoSystem>();
        var user = new NetUserId(Guid.NewGuid());

        MapId mapId = default;
        Entity<MapGridComponent> grid = default;
        var restored = EntityUid.Invalid;
        var proto = string.Empty;
        var stage = EntityLifeStage.Terminating;
        var onGrid = false;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out mapId, runMapInit: false);
            grid = mapMan.CreateGridEntity(mapId);
            Fill(mapSys, grid);

            var trader = entMan.SpawnEntity(TraderProto, new EntityCoordinates(grid.Owner, new Vector2(1.5f, 1.5f)));
            undo.RecordErased(user, mapSys.GetMap(mapId), (trader, entMan.GetComponent<TransformComponent>(trader)));
            entMan.DeleteEntity(trader);
        });

        await server.WaitPost(() =>
        {
            undo.Apply(user, 1, redo: false);

            restored = undo.GetSteps(user, redo: true).Last().Entities[0].Handle.Uid;
            if (entMan.EntityExists(restored))
            {
                proto = entMan.GetComponent<MetaDataComponent>(restored).EntityPrototype?.ID ?? string.Empty;
                stage = entMan.GetComponent<MetaDataComponent>(restored).EntityLifeStage;
                onGrid = entMan.GetComponent<TransformComponent>(restored).ParentUid == grid.Owner;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(proto, Is.EqualTo(TraderProto), "The trader should have come back as itself.");
            Assert.That(stage, Is.LessThan(EntityLifeStage.MapInitialized), "The loadout must stay unequipped on a pre-init map.");
            Assert.That(onGrid, Is.True, "The trader should be back on the grid it stood on.");
        });

        await server.WaitPost(() => mapSys.DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }

    /// <summary>A tile change undoes to the tile that was underneath.</summary>
    [Test]
    public async Task TileUndoRestoresOldTile()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var undo = entMan.System<MappingUndoSystem>();
        var user = new NetUserId(Guid.NewGuid());

        MapId mapId = default;
        Entity<MapGridComponent> grid = default;
        var indices = new Vector2i(0, 0);
        var oldId = 0;
        var newId = 0;
        var afterPlace = 0;
        var afterUndo = 0;
        var afterRedo = 0;
        var steps = 0;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out mapId, runMapInit: false);
            grid = mapMan.CreateGridEntity(mapId);
            Fill(mapSys, grid);

            oldId = mapSys.GetTileRef(grid, indices).Tile.TypeId;
            newId = tileDefs["FloorSteel"].TileId;

            // What PlaceNewTile does: change the tile, then say who did it.
            mapSys.SetTile(grid, indices, new Tile(newId));
            var ev = new PlacementTileEvent(newId, mapSys.GridTileToLocal(grid.Owner, grid.Comp, indices), user);
            entMan.EventBus.RaiseEvent(EventSource.Local, ev);

            afterPlace = mapSys.GetTileRef(grid, indices).Tile.TypeId;
            steps = undo.GetCounts(user).Undo;
        });

        await server.WaitPost(() =>
        {
            undo.Apply(user, 1, redo: false);
            afterUndo = mapSys.GetTileRef(grid, indices).Tile.TypeId;
        });

        await server.WaitPost(() =>
        {
            undo.Apply(user, 1, redo: true);
            afterRedo = mapSys.GetTileRef(grid, indices).Tile.TypeId;
        });

        Assert.Multiple(() =>
        {
            Assert.That(steps, Is.EqualTo(1), "The tile change should have been recorded.");
            Assert.That(afterPlace, Is.EqualTo(newId));
            Assert.That(afterUndo, Is.EqualTo(oldId), "Undo should have put the old tile back.");
            Assert.That(afterRedo, Is.EqualTo(newId), "Redo should have put the new tile back.");
        });

        await server.WaitPost(() => mapSys.DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }

    /// <summary>A burst in one tick is one step, so a dragged line comes back in one go.</summary>
    [Test]
    public async Task BurstUndoesAsOneStep()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var undo = entMan.System<MappingUndoSystem>();
        var user = new NetUserId(Guid.NewGuid());

        MapId mapId = default;
        Entity<MapGridComponent> grid = default;
        var walls = new EntityUid[3];
        var steps = 0;
        var recorded = 0;
        var remaining = 0;
        var leftAlive = 0;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out mapId, runMapInit: false);
            grid = mapMan.CreateGridEntity(mapId);
            Fill(mapSys, grid);

            var mapUid = mapSys.GetMap(mapId);
            for (var i = 0; i < walls.Length; i++)
            {
                walls[i] = entMan.SpawnEntity(WallProto, new EntityCoordinates(grid.Owner, new Vector2(i + 0.5f, 0.5f)));
                undo.RecordCreated(user, mapUid, (walls[i], entMan.GetComponent<TransformComponent>(walls[i])));
            }

            steps = undo.GetCounts(user).Undo;
            recorded = undo.GetSteps(user, redo: false).Last().Entities.Count;
        });

        await server.WaitPost(() =>
        {
            undo.Apply(user, 1, redo: false);
            leftAlive = walls.Count(entMan.EntityExists);
            remaining = undo.GetCounts(user).Undo;
        });

        Assert.Multiple(() =>
        {
            Assert.That(steps, Is.EqualTo(1), "Three placements in one tick should be one step.");
            Assert.That(recorded, Is.EqualTo(3));
            Assert.That(leftAlive, Is.Zero, "One undo should have taken the whole burst away.");
            Assert.That(remaining, Is.Zero);
        });

        await server.WaitPost(() => mapSys.DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }

    /// <summary>Nothing is recorded once a map has been map-initialised, so ordinary rounds are untouched.</summary>
    [Test]
    public async Task InitialisedMapRecordsNothing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var undo = entMan.System<MappingUndoSystem>();
        var user = new NetUserId(Guid.NewGuid());

        MapId mapId = default;
        Entity<MapGridComponent> grid = default;
        var counts = (1, 1);

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out mapId);
            grid = mapMan.CreateGridEntity(mapId);
            Fill(mapSys, grid);

            var wall = entMan.SpawnEntity(WallProto, new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
            var ev = new PlacementEntityEvent(wall,
                entMan.GetComponent<TransformComponent>(wall).Coordinates,
                PlacementEventAction.Create,
                user);
            entMan.EventBus.RaiseEvent(EventSource.Local, ev);

            counts = undo.GetCounts(user);
        });

        Assert.That(counts, Is.EqualTo((0, 0)), "Placement on a live map must never be recorded.");

        await server.WaitPost(() => mapSys.DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }

    private static void Fill(SharedMapSystem mapSys, Entity<MapGridComponent> grid)
    {
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        mapSys.SetTiles(grid.Owner, grid.Comp, tiles);
    }
}
