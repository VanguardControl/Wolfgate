#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingMode;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network.Messages;
using Robust.Shared.Placement;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// Placement and tool edits stop at the session's grid, entity, per-tile, area and level caps, refuse denied
/// prototypes, and drop requests past the edit rate.
/// </summary>
[TestFixture]
public sealed class WFMappingLimitsTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFMappingModeLimitItem

- type: entity
  id: WFMappingModeLimitRule
  components:
  - type: GameRule
";

    private static MsgPlacement PlaceEntity(NetCoordinates coords, string prototype) => new()
    {
        PlaceType = PlacementManagerMessage.RequestPlacement,
        EntityTemplateName = prototype,
        NetCoordinates = coords,
        Align = "None",
    };

    private static MsgPlacement PlaceTile(NetCoordinates coords, int tile) => new()
    {
        PlaceType = PlacementManagerMessage.RequestPlacement,
        IsTile = true,
        TileType = tile,
        NetCoordinates = coords,
        Align = "None",
    };

    [Test]
    public async Task Caps()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.CfgMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var players = await AddMappers(pair, 2);
        var (a, b) = (players[0], players[1]);

        var plating = tileDefs["Plating"].TileId;
        WFMappingSession session = default!;
        EntityUid grid = default;

        NetCoordinates OnMap(float x, float y) =>
            entMan.GetNetCoordinates(new EntityCoordinates(session.RootMap, new Vector2(x, y)));

        WFMappingCanEditEvent Check(WFMappingEditKind kind, EntityUid? onGrid = null) =>
            new(a, session.RootMap, kind) { Grid = onGrid, Quiet = true };

        WFMappingCanEditEvent Raise(WFMappingCanEditEvent ev)
        {
            entMan.EventBus.RaiseEvent(EventSource.Local, ev);
            return ev;
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Limits Test", out var created), Is.True);
            session = created!;
            var newGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            grid = newGrid.Owner;
            mapSys.SetTiles(grid, newGrid.Comp,
                Enumerable.Range(0, 4).Select(x => (new Vector2i(x, 0), new Tile(plating))).ToList());
        });

        await server.WaitAssertion(() =>
        {
            // Grids: one allowed, so a tile off the grid would make a second and is refused.
            cfg.SetCVar(WFMappingModeCVars.GridsPerPlayer, 1);
            cfg.SetCVar(WFMappingModeCVars.MaxGridsPerSession, 1);
            Assert.Multiple(() =>
            {
                Assert.That(mapping.GridCap(session), Is.EqualTo(1));
                Assert.That(mapping.AllowPlacement(a, PlaceTile(OnMap(1.5f, 0.5f), plating)), Is.True, "On the grid.");
                Assert.That(mapping.AllowPlacement(a, PlaceTile(OnMap(20.5f, 20.5f), plating)), Is.False, "A new grid.");
                Assert.That(Raise(Check(WFMappingEditKind.Paste, grid)).Cancelled, Is.False, "A paste onto the grid.");
            });

            var paste = Check(WFMappingEditKind.Paste);
            paste.NewGrid = true;
            paste.Cells = new Box2i(20, 20, 21, 21);
            Assert.That(Raise(paste).Reason, Is.EqualTo("wf-mapping-mode-limit-grids"));
            cfg.SetCVar(WFMappingModeCVars.GridsPerPlayer, WFMappingModeCVars.GridsPerPlayer.DefaultValue);
            cfg.SetCVar(WFMappingModeCVars.MaxGridsPerSession, WFMappingModeCVars.MaxGridsPerSession.DefaultValue);

            // Area: every tile lies in the map_size square round the origin.
            cfg.SetCVar(WFMappingModeCVars.MapSize, 64);
            Assert.Multiple(() =>
            {
                Assert.That(mapping.AllowPlacement(a, PlaceTile(OnMap(31.5f, 0.5f), plating)), Is.True);
                Assert.That(mapping.AllowPlacement(a, PlaceTile(OnMap(32.5f, 0.5f), plating)), Is.False);
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(-33f, 0.5f), "WFMappingModeLimitItem")), Is.False);
            });

            var move = Check(WFMappingEditKind.Edit, grid);
            move.Cells = new Box2i(30, 0, 34, 1);
            Assert.That(Raise(move).Reason, Is.EqualTo("wf-mapping-mode-limit-area"));
            cfg.SetCVar(WFMappingModeCVars.MapSize, WFMappingModeCVars.MapSize.DefaultValue);

            // Per tile: the cell already holding the cap is full, its neighbour isn't.
            cfg.SetCVar(WFMappingModeCVars.MaxEntitiesPerTile, 2);
            for (var i = 0; i < 2; i++)
            {
                entMan.SpawnEntity("WFMappingModeLimitItem", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
            }

            Assert.Multiple(() =>
            {
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(2.5f, 0.5f), "WFMappingModeLimitItem")), Is.False);
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(3.5f, 0.5f), "WFMappingModeLimitItem")), Is.True);
            });

            var crowded = Check(WFMappingEditKind.Paste, grid);
            crowded.Cells = new Box2i(2, 0, 3, 1);
            crowded.Entities = 1;
            crowded.EntityCells = new List<Vector2i> { new(2, 0) };
            Assert.That(Raise(crowded).Reason, Is.EqualTo("wf-mapping-mode-limit-entities-per-tile"));
            cfg.SetCVar(WFMappingModeCVars.MaxEntitiesPerTile, WFMappingModeCVars.MaxEntitiesPerTile.DefaultValue);

            // Session total: at the cap nothing more goes down.
            session.CountedAt = TimeSpan.Zero;
            var count = mapping.EntityCount(session);
            cfg.SetCVar(WFMappingModeCVars.MaxEntities, count);
            Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(1.5f, 0.5f), "WFMappingModeLimitItem")), Is.False);
            var big = Check(WFMappingEditKind.Paste, grid);
            big.Entities = 1;
            Assert.That(Raise(big).Reason, Is.EqualTo("wf-mapping-mode-limit-entities"));
            cfg.SetCVar(WFMappingModeCVars.MaxEntities, WFMappingModeCVars.MaxEntities.DefaultValue);

            // Prototypes carrying a denied component are refused.
            Assert.Multiple(() =>
            {
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(1.5f, 0.5f), "WFMappingModeLimitRule")), Is.False);
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(1.5f, 0.5f), "WFMappingModeLimitItem")), Is.True);
            });

            // Levels: at max_levels another is refused.
            cfg.SetCVar(WFMappingModeCVars.MaxLevels, 1);
            Assert.That(Raise(Check(WFMappingEditKind.AddLevel)).Reason, Is.EqualTo("wf-mapping-mode-limit-levels"));
            cfg.SetCVar(WFMappingModeCVars.MaxLevels, 2);
            Assert.That(Raise(Check(WFMappingEditKind.AddLevel)).Cancelled, Is.False);
            cfg.SetCVar(WFMappingModeCVars.MaxLevels, WFMappingModeCVars.MaxLevels.DefaultValue);
        });

        // Throttle: a fresh mapper gets max_edits_per_second at once, then nothing until it refills.
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryJoinSession(b, session), Is.True);
            cfg.SetCVar(WFMappingModeCVars.MaxEditsPerSecond, 3);
            var allowed = Enumerable.Range(0, 6)
                .Count(_ => mapping.AllowPlacement(b, PlaceEntity(OnMap(0.5f, 0.5f), "WFMappingModeLimitItem")));
            Assert.That(allowed, Is.EqualTo(3));
        });

        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.AllowPlacement(b, PlaceEntity(OnMap(0.5f, 0.5f), "WFMappingModeLimitItem")), Is.True,
                "The bucket refills.");
            cfg.SetCVar(WFMappingModeCVars.MaxEditsPerSecond, WFMappingModeCVars.MaxEditsPerSecond.DefaultValue);
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
