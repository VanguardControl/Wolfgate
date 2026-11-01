#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.StationRecords.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Content.Shared.StationRecords;
using Content.Shared.Store.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.LiveFixes;

/// <summary>Components in the state that failed live ship copies must save without being stripped.</summary>
[TestFixture]
public sealed class ShipCopySaveTest
{
    [Test]
    public async Task ReportedComponentsSave()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        var shipyard = entMan.System<ShipyardSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));

            var lathe = entMan.SpawnEntity("Autolathe", coords);
            var recipe = server.ProtoMan.EnumeratePrototypes<LatheRecipePrototype>().First();
            entMan.GetComponent<LatheComponent>(lathe).Queue.Add(new LatheRecipeBatch(recipe, 0, 1, null));

            var console = entMan.SpawnEntity("Paper", coords);
            // The field is restricted to the console's own system.
            typeof(GeneralStationRecordConsoleComponent).GetField(nameof(GeneralStationRecordConsoleComponent.Filter))!
                .SetValue(entMan.AddComponent<GeneralStationRecordConsoleComponent>(console),
                    new StationRecordsFilter(StationRecordFilterType.Name, "x"));

            var rod = entMan.SpawnEntity("Paper", coords);
            entMan.EnsureComponent<DamageOnInteractComponent>(rod);

            // Same store as CrateSyndicateSurplusBundle; its random contents aren't what's being tested.
            var store = entMan.SpawnEntity("Paper", coords);
            entMan.EnsureComponent<StoreComponent>(store).AccountOwner = entMan.SpawnEntity(null, MapCoordinates.Nullspace);

            Assert.That(shipyard.TrySaveShip(grid.Owner, out var data), Is.True);
            Assert.That(entMan.HasComponent<LatheComponent>(lathe), "The lathe should keep its lathe component.");
            Assert.That(shipyard.TryAddSavedShip(data!, out var loaded), Is.True);
            entMan.DeleteEntity(loaded!.Value);
        });

        await pair.CleanReturnAsync();
    }
}
