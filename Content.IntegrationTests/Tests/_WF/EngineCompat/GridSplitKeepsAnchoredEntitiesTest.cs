#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.EngineCompat;

/// <summary>Anchored entities on a split-off piece of grid survive the engine re-anchoring them.</summary>
[TestFixture]
public sealed class GridSplitKeepsAnchoredEntitiesTest
{
    private const string Cable = "CableHV";

    [Test]
    public async Task SplitOffCableSurvives()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        var grid = EntityUid.Invalid;
        var cables = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CVars.GridSplitting, true);
            var created = maps.CreateGridEntity(map.MapId);
            grid = created.Owner;
            server.System<SharedTransformSystem>().SetLocalPosition(grid, new Vector2(30f, 30f));
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 7; x++)
            {
                tiles.Add((new Vector2i(x, 0), map.Tile.Tile));
            }

            maps.SetTiles(grid, created.Comp, tiles);

            foreach (var x in new[] { 0, 1, 5, 6 })
            {
                cables.Add(em.SpawnEntity(Cable, new EntityCoordinates(grid, x + 0.5f, 0.5f)));
            }
        });

        await server.WaitRunTicks(5);

        await server.WaitPost(() => maps.SetTile(grid, em.GetComponent<MapGridComponent>(grid), new Vector2i(3, 0), Tile.Empty));

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            foreach (var cable in cables)
            {
                Assert.That(em.EntityExists(cable), $"{cable} was deleted by the split.");
                Assert.That(em.GetComponent<TransformComponent>(cable).Anchored, $"{cable} was left unanchored by the split.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
