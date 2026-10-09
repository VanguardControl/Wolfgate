using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Shuttles.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Shuttles;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Shuttles.Components;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>
/// The whole-ship view's damage overlay is driven by a server sweep of anchored structures.
/// </summary>
public sealed class ShipStatusTest
{
    [Test]
    public async Task DamagedWallIsReported()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var damageSystem = entManager.System<DamageableSystem>();
        var statusSystem = entManager.System<ShipStatusSystem>();

        await server.WaitAssertion(() =>
        {
            entManager.DeleteEntity(map.Grid);

            var grid = mapManager.CreateGridEntity(map.MapId);

            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);

            var console = entManager.SpawnEntity("ComputerShuttle",
                new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));

            var wall = entManager.SpawnEntity("WallSolid",
                new EntityCoordinates(grid.Owner, new Vector2(2.5f, 2.5f)));

            var wallTile = mapSystem.TileIndicesFor(grid.Owner, grid.Comp,
                entManager.GetComponent<TransformComponent>(wall).Coordinates);

            // An undamaged hull should report nothing at all.
            var clean = statusSystem.GetStatus(console);
            Assert.That(clean, Is.Not.Null, "The console should resolve to its own grid.");
            Assert.That(clean!.Tiles.Any(t => (t.Flags & ShipTileFlags.Damaged) != 0), Is.False,
                "An intact hull should report no damaged tiles.");

            Assert.That(clean.Summary.HullIntegrity, Is.EqualTo(1f));

            // Hit the wall hard enough to be well past the reporting threshold but not destroy it.
            var damage = new DamageSpecifier(protoManager.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(100));
            damageSystem.TryChangeDamage(wall, damage, ignoreResistances: true);

            var damaged = statusSystem.GetStatus(console);
            Assert.That(damaged, Is.Not.Null);

            var reported = damaged!.Tiles.FirstOrDefault(t => t.Index == wallTile);
            Assert.That(reported.Flags & ShipTileFlags.Damaged, Is.Not.EqualTo(ShipTileFlags.None),
                "The damaged wall's tile should be flagged.");
            Assert.That(damaged.Summary.DamagedTiles, Is.GreaterThan(0));
            Assert.That(damaged.Summary.WorstIntegrity, Is.LessThan(1f));
            Assert.That(damaged.Summary.HullIntegrity, Is.LessThan(1f));
            Assert.That(damaged.Summary.HullIntegrity, Is.GreaterThan(damaged.Summary.WorstIntegrity),
                "The whole-hull condition averages healthy locations as well as damaged ones.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DestroyedStructuresAndFloorRemainLostUntilTheirLocationsAreRepaired()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = em.System<SharedMapSystem>();
        var status = em.System<ShipStatusSystem>();
        var wall = EntityUid.Invalid;
        var console = EntityUid.Invalid;
        var wallPosition = new EntityCoordinates(map.Grid.Owner, new Vector2(3.5f, 1.5f));
        var missingFloor = new Vector2i(4, 2);

        await server.WaitAssertion(() =>
        {
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 3; y++)
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);
            console = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f)));
            wall = em.SpawnEntity("WallSolid", wallPosition);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));

            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var damage = new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(1400));
            em.System<DamageableSystem>().TryChangeDamage(wall, damage, ignoreResistances: true);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, missingFloor, Tile.Empty);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(wall), Is.True, "Native destruction must remove the wall, as an impact does.");
            var lost = status.GetStatus(console)!.Summary;
            Assert.That(lost.WorstIntegrity, Is.EqualTo(1f), "The surviving structures are intact.");
            Assert.That(lost.HullIntegrity, Is.EqualTo(13f / 15f).Within(0.001f),
                "The destroyed wall and missing deck both remain in the surveyed hull.");

            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(5, 0), map.Tile.Tile);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(14f / 16f).Within(0.001f),
                "New construction adds hull without forgetting losses at old locations.");

            var replacement = em.SpawnEntity("WallSolid", wallPosition);
            Assert.That(replacement, Is.Not.EqualTo(wall));
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(15f / 16f).Within(0.001f),
                "A replacement structure restores its original location while the deck hole remains.");
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, missingFloor, map.Tile.Tile);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "Repairing every surveyed location restores the hull.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PilotEntrySurveysHullBeforeAConsoleBearingFragmentSplitsAway()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.GridSplitting, true));
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var maps = em.System<SharedMapSystem>();
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 2; y++)
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);
            var position = new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f));
            var console = em.SpawnEntity("ComputerShuttle", position);
            var pilot = em.SpawnEntity("MobHuman", position);
            em.EnsureComponent<PilotComponent>(pilot);
            em.System<ShuttleConsoleSystem>().AddPilot(console, pilot, em.GetComponent<ShuttleConsoleComponent>(console));

            // No status page has been opened before the impact separates the smaller, piloted section.
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(1, 0), Tile.Empty),
                (new Vector2i(1, 1), Tile.Empty),
            });
            var fragment = em.GetComponent<TransformComponent>(console).GridUid;
            Assert.That(fragment, Is.Not.Null);
            Assert.That(fragment, Is.Not.EqualTo(map.Grid.Owner), "The console must be on the newly created smaller grid.");
            var originalConsole = em.SpawnEntity("ComputerShuttle",
                new EntityCoordinates(map.Grid.Owner, new Vector2(2.5f, 0.5f)));
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(5, 0), map.Tile.Tile);
            var status = em.System<ShipStatusSystem>();
            Assert.That(status.GetStatus(originalConsole)!.Summary.HullIntegrity, Is.EqualTo(7f / 11f).Within(0.001f),
                "Construction on the original grid updates its survey without modifying the fragment's inherited snapshot.");
            var summary = status.GetStatus(console)!.Summary;
            Assert.That(summary.WorstIntegrity, Is.EqualTo(1f));
            Assert.That(summary.HullIntegrity, Is.EqualTo(2f / 10f).Within(0.001f),
                "The new fragment inherits the original hull baseline instead of resetting to a healthy ship.");
        });
        await pair.CleanReturnAsync();
    }
}
