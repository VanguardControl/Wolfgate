#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Maps;
using Content.Shared.NodeContainer;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingTools;

/// <summary>
/// Pipe mode's draws on the server: a path becomes the right plain pipes, joins and upgrades the pipes around it,
/// is painted, paints whole networks, undoes as one edit and stops at a mapper's limits.
/// </summary>
[TestFixture]
public sealed class MappingToolsPipesTest
{
    private static readonly Color Distro = Color.FromHex("#0055CC");
    private static readonly Color Waste = Color.FromHex("#990000");

    private readonly record struct PipeInfo(string? Id, PipeDirection Directions, Color? Color, int Count);

    private static PipeInfo PipeAt(IEntityManager entMan, EntityUid grid, Vector2i cell,
        AtmosPipeLayer layer = AtmosPipeLayer.Primary)
    {
        var mapSys = entMan.System<MapSystem>();
        var anchored = new List<EntityUid>();
        mapSys.GetAnchoredEntities((grid, entMan.GetComponent<MapGridComponent>(grid)), cell, anchored);

        string? id = null;
        Color? color = null;
        var directions = PipeDirection.None;
        var count = 0;
        foreach (var uid in anchored)
        {
            if (!entMan.TryGetComponent(uid, out NodeContainerComponent? nodes))
                continue;

            foreach (var node in nodes.Nodes.Values)
            {
                if (node is not PipeNode pipe || pipe.CurrentPipeLayer != layer)
                    continue;

                directions |= pipe.CurrentPipeDirection;
                id = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                color = entMan.TryGetComponent(uid, out AtmosPipeColorComponent? paint) ? paint.Color : null;
                count++;
            }
        }

        return new PipeInfo(id, directions, color, count);
    }

    private static List<Vector2i> Run(int x0, int x1, int y)
    {
        return Enumerable.Range(x0, x1 - x0 + 1).Select(x => new Vector2i(x, y)).ToList();
    }

    private static MappingToolsDrawEvent Pipes(NetEntity grid, List<Vector2i> cells, WFMappingPipeSpec spec,
        MappingDrawOp op = MappingDrawOp.Pipes)
    {
        return new MappingToolsDrawEvent(grid, op, cells, null, 0, null, Angle.Zero) { Pipes = spec };
    }

    [Test]
    public async Task DrawJoinPaintUndo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var testMap = await pair.CreateTestMap();
        var mapSys = entMan.System<MapSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var session = pair.Player!;
        var grid = testMap.Grid;
        var net = entMan.GetNetEntity(grid.Owner);
        var distro = new WFMappingPipeSpec(AtmosPipeLayer.Primary, Distro, false, true, true);
        var waste = distro with { Color = Waste };

        PipeInfo At(int x, int y, AtmosPipeLayer layer = AtmosPipeLayer.Primary) =>
            PipeAt(entMan, grid, new Vector2i(x, y), layer);

        await server.WaitAssertion(() =>
        {
            grid.Comp.CanSplit = false;
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(-2, 16)
                .SelectMany(x => Enumerable.Range(-2, 12).Select(y => (new Vector2i(x, y), plating)))
                .ToList());

            // A straight run: halves at the ends, straights between, all painted.
            tools.Draw(session, Pipes(net, Run(0, 4, 0), distro));
            Assert.Multiple(() =>
            {
                Assert.That(At(0, 0), Is.EqualTo(new PipeInfo("GasPipeHalf", PipeDirection.East, Distro, 1)));
                for (var x = 1; x <= 3; x++)
                {
                    Assert.That(At(x, 0), Is.EqualTo(new PipeInfo("GasPipeStraight", PipeDirection.Lateral, Distro, 1)), $"{x}");
                }

                Assert.That(At(4, 0), Is.EqualTo(new PipeInfo("GasPipeHalf", PipeDirection.West, Distro, 1)));
            });

            // An L turns with a bend.
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(0, 3), new(1, 3), new(1, 4) }, distro));
            Assert.Multiple(() =>
            {
                Assert.That(At(1, 3).Id, Is.EqualTo("GasPipeBend"));
                Assert.That(At(1, 3).Directions, Is.EqualTo(PipeDirection.NWBend));
                Assert.That(At(0, 3).Directions, Is.EqualTo(PipeDirection.East));
                Assert.That(At(1, 4).Directions, Is.EqualTo(PipeDirection.South));
            });

            // A run starting beside an existing straight turns it into a T.
            var straight = entMan.SpawnEntity("GasPipeStraight", new EntityCoordinates(grid, new Vector2(8.5f, 0.5f)));
            Assert.That(entMan.GetComponent<TransformComponent>(straight).Anchored, Is.True);
            Assert.That(At(8, 0).Directions, Is.EqualTo(PipeDirection.Longitudinal));
            tools.Draw(session, Pipes(net, Run(9, 11, 0), waste));
            Assert.Multiple(() =>
            {
                Assert.That(At(8, 0), Is.EqualTo(new PipeInfo("GasPipeTJunction", PipeDirection.TEast, Waste, 1)));
                Assert.That(At(9, 0).Directions, Is.EqualTo(PipeDirection.Lateral));
                Assert.That(At(11, 0).Directions, Is.EqualTo(PipeDirection.West));
            });

            // A second network, apart from the first.
            tools.Draw(session, Pipes(net, Run(0, 3, 6), distro));
            Assert.That(At(2, 6).Color, Is.EqualTo(Distro));
        });

        await server.WaitAssertion(() =>
        {
            // Painting a pipe paints its run, not the run next door or the L.
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(2, 0) }, waste, MappingDrawOp.PipeColor));
            Assert.Multiple(() =>
            {
                for (var x = 0; x <= 4; x++)
                {
                    Assert.That(At(x, 0).Color, Is.EqualTo(Waste), $"{x}");
                }

                Assert.That(At(2, 6).Color, Is.EqualTo(Distro), "A network that doesn't touch keeps its colour.");
                Assert.That(At(1, 3).Color, Is.EqualTo(Distro));
            });

            // Undo walks back each edit whole.
            tools.StepHistory(session, redo: false);
            Assert.That(Run(0, 4, 0).All(c => At(c.X, c.Y).Color == Distro), Is.True, "Undo puts the old colour back.");

            tools.StepHistory(session, redo: false);
            Assert.That(Run(0, 3, 6).All(c => At(c.X, c.Y).Count == 0), Is.True, "Undo removes the drawn run.");

            tools.StepHistory(session, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(At(8, 0).Id, Is.EqualTo("GasPipeStraight"), "The T is a straight again.");
                Assert.That(At(8, 0).Directions, Is.EqualTo(PipeDirection.Longitudinal));
                Assert.That(At(8, 0).Color, Is.EqualTo(Color.White));
                Assert.That(Run(9, 11, 0).All(c => At(c.X, c.Y).Count == 0), Is.True);
            });

            tools.StepHistory(session, redo: true);
            Assert.That(At(8, 0), Is.EqualTo(new PipeInfo("GasPipeTJunction", PipeDirection.TEast, Waste, 1)),
                "Redo builds the T again.");
        });

        await server.WaitAssertion(() =>
        {
            // The second layer uses its own prototypes and leaves the first layer alone.
            var second = distro with { Layer = AtmosPipeLayer.Secondary };
            tools.Draw(session, Pipes(net, Run(0, 2, 8), second));
            Assert.Multiple(() =>
            {
                Assert.That(At(0, 8, AtmosPipeLayer.Secondary).Id, Is.EqualTo("GasPipeHalfAlt1"));
                Assert.That(At(1, 8, AtmosPipeLayer.Secondary), Is.EqualTo(
                    new PipeInfo("GasPipeStraightAlt1", PipeDirection.Lateral, Distro, 1)));
                Assert.That(At(1, 8).Count, Is.Zero);
            });

            // A path across a pipe that isn't replaced keeps that pipe.
            var keep = distro with { Replace = false, Color = Waste };
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(2, -1), new(2, 0), new(2, 1) }, keep));
            Assert.Multiple(() =>
            {
                Assert.That(At(2, 0).Id, Is.EqualTo("GasPipeStraight"), "Replace off leaves the pipe.");
                Assert.That(At(2, 0).Color, Is.EqualTo(Distro));
            });

            // With replace on the crossing becomes a four-way.
            var cross = distro with { Color = Waste };
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(2, -1), new(2, 0), new(2, 1) }, cross));
            Assert.Multiple(() =>
            {
                Assert.That(At(2, 0), Is.EqualTo(new PipeInfo("GasPipeFourway", PipeDirection.Fourway, Waste, 1)));
                Assert.That(At(2, -1).Directions, Is.EqualTo(PipeDirection.North));
                Assert.That(At(2, 1).Directions, Is.EqualTo(PipeDirection.South));
            });

            // Auto takes the colour of the pipe it joins.
            var auto = distro with { Color = null, AutoColor = true };
            tools.Draw(session, Pipes(net, Run(12, 13, 0), auto));
            Assert.That(At(13, 0).Color, Is.EqualTo(Waste), "The run joins the waste pipe at 11.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MapperPipeLimits()
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
        var spec = new WFMappingPipeSpec(AtmosPipeLayer.Secondary, Distro, false, true, true);

        PipeInfo At(int x, int y) => PipeAt(entMan, grid, new Vector2i(x, y), AtmosPipeLayer.Secondary);

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Pipe Limits", out var created), Is.True);
            var newGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(created!.RootMap).MapId);
            grid = newGrid.Owner;
            newGrid.Comp.CanSplit = false;
            mapSys.SetTiles(grid, newGrid.Comp, Enumerable.Range(0, 8)
                .SelectMany(x => Enumerable.Range(0, 4).Select(y => (new Vector2i(x, y), new Tile(tileDefs["Plating"].TileId))))
                .ToList());
        });

        await server.WaitAssertion(() =>
        {
            var net = entMan.GetNetEntity(grid);

            // A mapper lays second-layer pipes on their paused map.
            tools.Draw(a, Pipes(net, Run(0, 2, 0), spec));
            Assert.Multiple(() =>
            {
                Assert.That(At(0, 0), Is.EqualTo(new PipeInfo("GasPipeHalfAlt1", PipeDirection.East, Distro, 1)));
                Assert.That(At(1, 0), Is.EqualTo(new PipeInfo("GasPipeStraightAlt1", PipeDirection.Lateral, Distro, 1)));
                Assert.That(At(2, 0), Is.EqualTo(new PipeInfo("GasPipeHalfAlt1", PipeDirection.West, Distro, 1)));
            });

            // max_selection_cells caps the path.
            cfg.SetCVar(WFMappingModeCVars.MaxSelectionCells, 2);
            tools.Draw(a, Pipes(net, Run(0, 3, 2), spec));
            Assert.That(Run(0, 3, 2).All(c => At(c.X, c.Y).Count == 0), Is.True, "Four cells is over the cap of two.");
            cfg.SetCVar(WFMappingModeCVars.MaxSelectionCells, WFMappingModeCVars.MaxSelectionCells.DefaultValue);

            // Pipes need a floor.
            tools.Draw(a, Pipes(net, Run(6, 9, 1), spec));
            Assert.That(At(6, 1).Count, Is.Zero, "A path running off the floor is refused whole.");

            // max_edit_entities caps a paint.
            cfg.SetCVar(WFMappingModeCVars.MaxEditEntities, 2);
            tools.Draw(a, Pipes(net, new List<Vector2i> { new(1, 0) }, spec with { Color = Waste }, MappingDrawOp.PipeColor));
            Assert.That(Run(0, 2, 0).All(c => At(c.X, c.Y).Color == Distro), Is.True, "Three pipes is over the cap of two.");
            cfg.SetCVar(WFMappingModeCVars.MaxEditEntities, WFMappingModeCVars.MaxEditEntities.DefaultValue);

            tools.Draw(a, Pipes(net, new List<Vector2i> { new(1, 0) }, spec with { Color = Waste }, MappingDrawOp.PipeColor));
            Assert.That(Run(0, 2, 0).All(c => At(c.X, c.Y).Color == Waste), Is.True, "Under the cap the paint goes through.");
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
