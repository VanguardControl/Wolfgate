#nullable enable
using Content.Shared._WF.PlanetCities;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetCities;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Station.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.Damage.Components;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetCities;

/// <summary>
/// Crocus is built on Asclepiu with every level on its own layer, its indestructible boundary made ordinary, its roads
/// joined to the world's, and its station joinable over the city's grids.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetCitySystem))]
public sealed class FixedCityTest
{
    private const int Radius = 384;

    [TestCase(false)]
    [TestCase(true)]
    public async Task CrocusStandsOnAsclepiuWithItsStation(bool caverns)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, ServerSeed = 1977 });
        var server = pair.Server;
        var entMan = server.EntMan;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        await PlanetFixture.EnableFeature(pair);

        var layers = new List<EntityUid>();
        var lower = new List<EntityUid>();
        var holds = new List<Task>();
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(CavernCVars.Caverns, caverns);
            server.CfgMan.SetCVar(PlanetCVars.FixedCities, true);
            var surface = prototypes.Index<WFPlanetSurfacePrototype>("WFSurfaceAsclepiu");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);
            Assert.That(network, Is.Not.Null);
            var comp = entMan.GetComponent<WFPlanetNetworkComponent>(network!.Value);
            layers.AddRange(comp.Layers);
            lower.AddRange(comp.LowerLayers);
            var preload = new WFPlanetPreloadStartingEvent(layers[0], Vector2.Zero, Radius, holds);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref preload);
        });

        for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
        {
            await pair.RunTicksSync(10);
        }

        await server.WaitAssertion(() =>
        {
            var ground = layers[0];
            var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
            Assert.That(record.GenerationError, Is.Null);
            Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True);

            var gasStation = record.Sites.SingleOrDefault(placed => placed.MapPath == "/Maps/_WF/PlanetPois/verda_gas_station.yml");
            Assert.That(gasStation, Is.Not.Null, "The gas station was not placed.");
            Assert.That(gasStation!.Connection, Is.EqualTo(WFPlanetPoiConnection.Roadside), "The gas station is off the roads.");

            var site = record.Sites.SingleOrDefault(site => site.Name == "wf-city-crocus");
            Assert.That(site, Is.Not.Null, "Crocus was not placed.");
            Assert.That(site!.Expeditions, Is.False, "A lived-in city is never an expedition site.");
            Assert.That(site.Connection, Is.Not.EqualTo(WFPlanetPoiConnection.Isolated), "Crocus's entrances did not take.");
            Assert.That(site.Sockets, Has.Count.EqualTo(2));
            foreach (var socket in site.Sockets)
            {
                Assert.That(record.RoadTiles.Contains(socket.Position + Step(socket.Direction))
                    || record.ShoulderTiles.Contains(socket.Position + Step(socket.Direction)), Is.True,
                    $"No world road meets Crocus's entrance at {socket.Position}.");
            }

            // One static, held-up level on each of the four flight layers.
            Assert.That(lower, Is.Not.Empty, "Asclepiu has no layer for Crocus's underground.");
            for (var depth = 1; depth <= 4; depth++)
            {
                var grids = Grids(entMan, layers[depth]).Where(entMan.HasComponent<WFPlanetCityGridComponent>).ToList();
                Assert.That(grids, Has.Count.EqualTo(1), $"Flight layer {depth} has no Crocus level.");
                Assert.That(entMan.HasComponent<CEZMappingAnchorGridComponent>(grids[0]), Is.True, $"Level {depth} would fall.");
                Assert.That(entMan.GetComponent<PhysicsComponent>(grids[0]).BodyType, Is.EqualTo(BodyType.Static), $"Level {depth} can drift.");
            }

            var underground = entMan.HasComponent<MapGridComponent>(lower[0])
                ? lower[0]
                : Grids(entMan, lower[0]).Single(entMan.HasComponent<WFPlanetCityGridComponent>);
            Assert.That(Children(entMan, underground).Count, Is.GreaterThan(1000), "Crocus's underground is missing.");

            // The boundary is ordinary rock and walls, nothing is godmoded and ghosts don't start here.
            var cityGrids = new List<EntityUid> { ground, underground };
            for (var depth = 1; depth <= 4; depth++)
            {
                cityGrids.AddRange(Grids(entMan, layers[depth]).Where(entMan.HasComponent<WFPlanetCityGridComponent>));
            }

            foreach (var grid in cityGrids)
            foreach (var child in Children(entMan, grid))
            {
                // Every layer is shared with the rest of the world; only Crocus's footprint is checked.
                var tile = entMan.GetComponent<TransformComponent>(child).LocalPosition.Floored();
                if (tile.X < site.Bounds.Left || tile.X >= site.Bounds.Right || tile.Y < site.Bounds.Bottom || tile.Y >= site.Bounds.Top)
                    continue;

                var id = entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID ?? string.Empty;
                Assert.That(id, Does.Not.Contain("Indestructible"), $"{id} is still indestructible.");
                Assert.That(id, Is.Not.EqualTo("SpawnPointObserver"));
                Assert.That(entMan.HasComponent<GodmodeComponent>(child), Is.False, $"{id} kept godmode.");
                Assert.That(id, Does.Not.Contain("Timed").And.Not.Contain("Warp").And.Not.Contain("BiomeSource").And.Not.Contain("Turret"),
                    $"{id} belongs to the ground war, not a city.");
            }

            // The station covers the city and offers its jobs.
            var station = entMan.GetComponent<WFPlanetCitiesComponent>(ground).Stations.Single();
            var stationGrids = entMan.GetComponent<StationDataComponent>(station).Grids;
            Assert.That(stationGrids, Does.Contain(ground));
            Assert.That(entMan.GetComponent<MetaDataComponent>(ground).EntityName, Does.Not.Contain("Jianghui"), "The station renamed the world.");
            Assert.That(stationGrids, Does.Contain(underground));
            var jobs = server.System<StationJobsSystem>();
            Assert.That(jobs.TryGetJobSlot(station, "CrocusDockworker", out _), Is.True, "Crocus's jobs are not open.");
            Assert.That(entMan.HasComponent<Content.Server._CE.ZLevels.Core.Components.CEStationZLevelsComponent>(station), Is.False,
                "The station would load Crocus's levels a second time.");
        });

        // Not torn down on a failure: the server is still busy and teardown would hide what failed.
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static Vector2i Step(WFPlanetPoiDirection direction) => direction switch
    {
        WFPlanetPoiDirection.North => new Vector2i(0, 1),
        WFPlanetPoiDirection.East => new Vector2i(1, 0),
        WFPlanetPoiDirection.South => new Vector2i(0, -1),
        _ => new Vector2i(-1, 0),
    };

    private static List<EntityUid> Grids(IEntityManager entMan, EntityUid map) =>
        Children(entMan, map).Where(entMan.HasComponent<MapGridComponent>).ToList();

    private static List<EntityUid> Children(IEntityManager entMan, EntityUid parent)
    {
        var result = new List<EntityUid>();
        var children = entMan.GetComponent<TransformComponent>(parent).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            result.Add(child);
        }

        return result;
    }
}
