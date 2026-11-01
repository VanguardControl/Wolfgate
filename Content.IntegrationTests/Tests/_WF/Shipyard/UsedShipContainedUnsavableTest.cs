#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Shipyard;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Shipyard;

/// <summary>A sold ship's containers must come back whole when they hold things that aren't normally saved.</summary>
[TestFixture]
public sealed class UsedShipContainedUnsavableTest
{
    private const string Crate = "CrateCybersunDarkGygaxBundle";
    private const string Mech = "MechGygaxSyndieFilled";

    [Test]
    public async Task MechInACrateComesAlong()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var grid = CreateGrid(entMan, map);
            entMan.SpawnEntity(Crate, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));

            var loaded = CopyAsSold(entMan, grid);
            Assert.That(CountAboard(entMan, loaded, Mech), Is.EqualTo(1));
            entMan.DeleteEntity(loaded);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MechInsideACarriedMechComesAlong()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var containers = entMan.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = CreateGrid(entMan, map);
            var coords = new EntityCoordinates(grid, new Vector2(0.5f, 0.5f));
            var mech = entMan.SpawnEntity(Mech, coords);
            var container = containers.EnsureContainer<Container>(mech, "wf_test");
            Assert.That(containers.Insert(entMan.SpawnEntity(Mech, coords), container));

            var loaded = CopyAsSold(entMan, grid);
            Assert.That(CountAboard(entMan, loaded, Mech), Is.EqualTo(2));
            entMan.DeleteEntity(loaded);
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid CreateGrid(IEntityManager entMan, Robust.UnitTesting.Pool.TestMapData map)
    {
        var maps = entMan.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(map.MapId);
        maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
        return grid.Owner;
    }

    /// <summary>Copies a grid the way a sale does, then loads the copy.</summary>
    private static EntityUid CopyAsSold(IEntityManager entMan, EntityUid grid)
    {
        var shipyard = entMan.System<ShipyardSystem>();
        var carry = entMan.System<UsedShipMarketSystem>().GetUnsavableAboard(grid);
        Assert.That(shipyard.TrySaveShip(grid, carry, out var data), Is.True);
        Assert.That(shipyard.TryAddSavedShip(data!, out var loaded), Is.True);
        return loaded!.Value;
    }

    private static int CountAboard(IEntityManager entMan, EntityUid grid, string proto)
    {
        var count = 0;
        var pending = new Queue<EntityUid>();
        pending.Enqueue(grid);
        while (pending.TryDequeue(out var uid))
        {
            var children = entMan.GetComponent<TransformComponent>(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                pending.Enqueue(child);
                if (entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID == proto)
                    count++;
            }
        }

        return count;
    }
}
