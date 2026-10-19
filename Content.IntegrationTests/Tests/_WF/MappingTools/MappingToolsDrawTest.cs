#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Server.Decals;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingTools;

/// <summary>
/// The mapping screen's draw request lays tiles, spawns entities and erases decals as one undoable edit, and a
/// mapper's draws stop at the selection, per-tile and prototype limits.
/// </summary>
[TestFixture]
public sealed class MappingToolsDrawTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFMappingDrawTestItem

- type: entity
  id: WFMappingDrawTestHidden
  categories: [ HideSpawnMenu ]

- type: entity
  id: WFMappingDrawTestRule
  components:
  - type: GameRule
";

    private static int CountOn(IEntityManager entMan, EntityUid grid, string prototype, Vector2i? cell = null)
    {
        var count = 0;
        var query = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID != prototype || xform.ParentUid != grid)
                continue;

            if (cell == null || MappingToolsMath.CellOf(xform.LocalPosition) == cell)
                count++;
        }

        return count;
    }

    [Test]
    public async Task DrawAndUndo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var testMap = await pair.CreateTestMap();
        var mapSys = entMan.System<MapSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var decals = entMan.System<DecalSystem>();
        var session = pair.Player!;
        var grid = testMap.Grid;
        var net = entMan.GetNetEntity(grid.Owner);

        string? TileAt(int x, int y) =>
            mapSys.TryGetTileRef(grid, grid, new Vector2i(x, y), out var tile) && !tile.Tile.IsEmpty
                ? tileDefs[tile.Tile.TypeId].ID
                : null;

        int DecalsAt(int x) => decals.GetDecalsInRange(grid, new Vector2(x + 0.5f, 0.5f), 0.4f).Count;

        MappingToolsDrawEvent Draw(MappingDrawOp op, List<Vector2i> cells, string? tile = null, string? prototype = null) =>
            new(net, op, cells, tile, 0, prototype, Angle.Zero);

        await server.WaitAssertion(() =>
        {
            grid.Comp.CanSplit = false;

            // Tiles over a 3x3 rectangle, then undo and redo.
            var rect = MappingToolsShapes.Rect(new Vector2i(10, 10), new Vector2i(12, 12), false);
            tools.Draw(session, Draw(MappingDrawOp.SetTiles, rect, tile: "FloorSteel"));
            Assert.That(rect.All(c => TileAt(c.X, c.Y) == "FloorSteel"), Is.True);

            tools.StepHistory(session, redo: false);
            Assert.That(rect.All(c => TileAt(c.X, c.Y) == null), Is.True, "Undo clears the drawn tiles.");

            tools.StepHistory(session, redo: true);
            Assert.That(rect.All(c => TileAt(c.X, c.Y) == "FloorSteel"), Is.True, "Redo lays them again.");

            // Only the border of a hollow rectangle changes; the inside keeps its tile.
            var hollow = MappingToolsShapes.Rect(new Vector2i(10, 10), new Vector2i(12, 12), true);
            tools.Draw(session, Draw(MappingDrawOp.SetTiles, hollow, tile: "Plating"));
            Assert.Multiple(() =>
            {
                Assert.That(hollow.All(c => TileAt(c.X, c.Y) == "Plating"), Is.True);
                Assert.That(TileAt(11, 11), Is.EqualTo("FloorSteel"));
            });

            // Space clears.
            tools.Draw(session, Draw(MappingDrawOp.SetTiles, new List<Vector2i> { new(11, 11) }, tile: "Space"));
            Assert.That(TileAt(11, 11), Is.Null);
            tools.StepHistory(session, redo: false);
            Assert.That(TileAt(11, 11), Is.EqualTo("FloorSteel"));
        });

        await server.WaitAssertion(() =>
        {
            // A line of entities on tiles: one per cell, gone again on undo.
            var line = MappingToolsShapes.Line(new Vector2i(10, 10), new Vector2i(14, 12));
            Assert.That(line, Has.Count.EqualTo(5));
            mapSys.SetTiles(grid, grid.Comp, line.Select(c => (c, new Tile(tileDefs["Plating"].TileId))).ToList());
            tools.Draw(session, Draw(MappingDrawOp.SpawnEntities, line, prototype: "WFMappingDrawTestItem"));
            Assert.Multiple(() =>
            {
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem"), Is.EqualTo(5));
                foreach (var cell in line)
                {
                    Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem", cell), Is.EqualTo(1), $"{cell}");
                }
            });

            tools.StepHistory(session, redo: false);
            Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem"), Is.Zero);

            // Bad requests: repeated cells, an unknown prototype, more cells than anyone may draw.
            var twice = new List<Vector2i> { new(10, 10), new(10, 10) };
            tools.Draw(session, Draw(MappingDrawOp.SpawnEntities, twice, prototype: "WFMappingDrawTestItem"));
            tools.Draw(session, Draw(MappingDrawOp.SpawnEntities, new List<Vector2i> { new(10, 10) }, prototype: "WFNoSuchPrototype"));
            var huge = MappingToolsShapes.Rect(new Vector2i(0, 0), new Vector2i(128, 128), false);
            Assert.That(huge, Has.Count.GreaterThan(16384));
            tools.Draw(session, Draw(MappingDrawOp.SetTiles, huge, tile: "Plating"));
            Assert.Multiple(() =>
            {
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem"), Is.Zero);
                Assert.That(TileAt(100, 100), Is.Null, "Over the hard cap nothing is drawn.");
            });
        });

        await server.WaitAssertion(() =>
        {
            // Decals on three cells; erasing two leaves the third, undo brings them back.
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(20, 3)
                .Select(x => (new Vector2i(x, 0), new Tile(tileDefs["Plating"].TileId))).ToList());
            for (var x = 20; x < 23; x++)
            {
                Assert.That(decals.TryAddDecal("Dirt", new EntityCoordinates(grid, x, 0), out _), Is.True);
            }

            tools.Draw(session, Draw(MappingDrawOp.EraseDecals, new List<Vector2i> { new(20, 0), new(21, 0) }));
            Assert.Multiple(() =>
            {
                Assert.That(DecalsAt(20), Is.Zero);
                Assert.That(DecalsAt(21), Is.Zero);
                Assert.That(DecalsAt(22), Is.EqualTo(1));
            });

            tools.StepHistory(session, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(DecalsAt(20), Is.EqualTo(1));
                Assert.That(DecalsAt(21), Is.EqualTo(1));
                Assert.That(DecalsAt(22), Is.EqualTo(1));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MapperLimits()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.CfgMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        EntityUid grid = default;
        var steel = tileDefs["FloorSteel"].TileId;

        bool IsSteel(Vector2i cell) =>
            mapSys.TryGetTileRef(grid, entMan.GetComponent<MapGridComponent>(grid), cell, out var tile) &&
            tile.Tile.TypeId == steel;

        MappingToolsDrawEvent Draw(MappingDrawOp op, List<Vector2i> cells, string? tile = null, string? prototype = null) =>
            new(entMan.GetNetEntity(grid), op, cells, tile, 0, prototype, Angle.Zero);

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Draw Limits", out var created), Is.True);
            var newGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(created!.RootMap).MapId);
            grid = newGrid.Owner;
            newGrid.Comp.CanSplit = false;
            mapSys.SetTiles(grid, newGrid.Comp, Enumerable.Range(0, 8)
                .SelectMany(x => Enumerable.Range(0, 4).Select(y => (new Vector2i(x, y), new Tile(tileDefs["Plating"].TileId))))
                .ToList());
        });

        await server.WaitAssertion(() =>
        {
            // max_selection_cells caps a draw's cells.
            cfg.SetCVar(WFMappingModeCVars.MaxSelectionCells, 4);
            var nine = MappingToolsShapes.Rect(new Vector2i(0, 0), new Vector2i(2, 2), false);
            tools.Draw(a, Draw(MappingDrawOp.SetTiles, nine, tile: "FloorSteel"));
            Assert.That(nine.Any(IsSteel), Is.False, "Nine cells is over the cap of four.");

            var four = MappingToolsShapes.Rect(new Vector2i(0, 0), new Vector2i(1, 1), false);
            tools.Draw(a, Draw(MappingDrawOp.SetTiles, four, tile: "FloorSteel"));
            Assert.That(four.All(IsSteel), Is.True, "Four cells fit.");

            var line = MappingToolsShapes.Line(new Vector2i(0, 3), new Vector2i(7, 3));
            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, line, prototype: "WFMappingDrawTestItem"));
            Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem"), Is.Zero, "Entity draws count their cells too.");
            cfg.SetCVar(WFMappingModeCVars.MaxSelectionCells, WFMappingModeCVars.MaxSelectionCells.DefaultValue);
        });

        await server.WaitAssertion(() =>
        {
            // The per-tile cap: a draw touching a full cell is refused whole.
            cfg.SetCVar(WFMappingModeCVars.MaxEntitiesPerTile, 2);
            for (var i = 0; i < 2; i++)
            {
                entMan.SpawnEntity("WFMappingDrawTestItem", new EntityCoordinates(grid, new Vector2(5.5f, 0.5f)));
            }

            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, new List<Vector2i> { new(5, 0), new(6, 0) },
                prototype: "WFMappingDrawTestItem"));
            Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem", new Vector2i(6, 0)), Is.Zero);

            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, new List<Vector2i> { new(6, 0), new(7, 0) },
                prototype: "WFMappingDrawTestItem"));
            Assert.Multiple(() =>
            {
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem", new Vector2i(6, 0)), Is.EqualTo(1));
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem", new Vector2i(7, 0)), Is.EqualTo(1));
            });
            cfg.SetCVar(WFMappingModeCVars.MaxEntitiesPerTile, WFMappingModeCVars.MaxEntitiesPerTile.DefaultValue);

            // Denied and hidden prototypes are refused.
            var cell = new List<Vector2i> { new(3, 1) };
            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, cell, prototype: "WFMappingDrawTestRule"));
            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, cell, prototype: "WFMappingDrawTestHidden"));
            Assert.Multiple(() =>
            {
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestRule"), Is.Zero, "A prototype with a denied component.");
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestHidden"), Is.Zero, "A HideSpawnMenu prototype.");
            });

            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, cell, prototype: "WFMappingDrawTestItem"));
            Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem", new Vector2i(3, 1)), Is.EqualTo(1));
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
