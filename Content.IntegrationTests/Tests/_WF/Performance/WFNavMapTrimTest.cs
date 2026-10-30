using Content.Server.Pinpointer;
using Content.Shared.Pinpointer;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>Checks the nav map's removal history stays bounded and a client behind it still gets everything.</summary>
[TestFixture]
[TestOf(typeof(NavMapSystem))]
public sealed class WFNavMapTrimTest
{
    private const int Spacing = 16;
    private const int Side = 20;

    [Test]
    public async Task ClientBehindTrimmedHistoryGetsFullState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var testMap = await pair.CreateTestMap();

        Entity<MapGridComponent> created = default;
        await server.WaitPost(() =>
        {
            created = maps.CreateGridEntity(testMap.MapId);
            var grid = created.Owner;
            for (var x = 0; x < Side; x++)
            {
                for (var y = 0; y < Side; y++)
                {
                    maps.SetTile(grid, created.Comp, new Vector2i(x * Spacing, y * Spacing), testMap.Tile.Tile);
                }
            }

            server.System<NavMapSystem>().EnsureGridNavMap(grid);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var grid = created.Owner;
            var nav = entMan.GetComponent<NavMapComponent>(grid);
            var before = server.Timing.CurTick;
            Assert.That(before, Is.GreaterThan(nav.CreationTick));

            // Leave one chunk standing, so the grid survives.
            for (var x = 0; x < Side; x++)
            {
                for (var y = 0; y < Side; y++)
                {
                    if (x != 0 || y != 0)
                        maps.SetTile(grid, created.Comp, new Vector2i(x * Spacing, y * Spacing), Tile.Empty);
                }
            }

            Assert.That(nav.WFRemovedChunks.Count, Is.LessThanOrEqualTo(NavMapSystem.WFRemovedLimit));
            Assert.That(nav.Chunks, Has.Count.EqualTo(1));

            var behind = entMan.GetComponentState(entMan.EventBus, nav, null, before);
            Assert.That(behind, Is.Not.InstanceOf<IComponentDeltaState>(),
                "A client behind the trimmed history can't be told what it missed by a delta.");

            var current = entMan.GetComponentState(entMan.EventBus, nav, null, new GameTick(nav.WFRemovedTrimmedTo.Value + 1));
            Assert.That(current, Is.InstanceOf<IComponentDeltaState>(), "A client past the trim should still get a delta.");

            entMan.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }
}
