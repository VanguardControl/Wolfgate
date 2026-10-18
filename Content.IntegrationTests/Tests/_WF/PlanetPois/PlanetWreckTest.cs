#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Parallax;
using Content.Server.Shuttles.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.Gravity;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Carcinoma's crashed ships: off the roads at a random angle, torn open, grounded, overgrown and inhabited.</summary>
[TestFixture]
public sealed class PlanetWreckTest
{
    [TestCase(1977)]
    [TestCase(8231)]
    public async Task CarcinomaHasOvergrownWrecks(int seed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, ServerSeed = seed });
        var server = pair.Server;
        var entities = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var holds = new List<Task>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        const int radius = 192;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, radius);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoisCarcinoma");
            var surface = prototypes.Index<WFPlanetSurfacePrototype>("WFSurfaceCarcinoma");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "WFSurfaceCarcinoma", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var preload = new WFPlanetPreloadStartingEvent(layers[0], Vector2.Zero, radius, holds);
            entities.EventBus.RaiseEvent(EventSource.Local, ref preload);
        });
        try
        {
            for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
                await pair.RunTicksSync(10);
            await pair.RunTicksSync(5);
            await server.WaitAssertion(() =>
            {
                var ground = layers[0];
                var record = entities.GetComponent<WFPlanetPoiComponent>(ground);
                Assert.That(record.GenerationError, Is.Null);
                var wreckSites = record.Sites.Where(site => site.Name == "wf-poi-wreck").ToList();
                Assert.That(wreckSites, Is.Not.Empty, "Carcinoma got no wrecks.");

                // Pinned ground round a wreck must have been laid first, or it stays a black ring.
                var groundGrid = entities.GetComponent<MapGridComponent>(ground);
                var biomes = server.System<BiomeSystem>();
                var groundBiome = (ground, entities.GetComponent<BiomeComponent>(ground));
                var holes = 0;
                foreach (var site in wreckSites)
                {
                    for (var x = site.Bounds.Left; x < site.Bounds.Right; x++)
                    for (var y = site.Bounds.Bottom; y < site.Bounds.Top; y++)
                    {
                        var index = new Vector2i(x, y);
                        if (biomes.WfIsPinned(groundBiome, index)
                            && server.System<SharedMapSystem>().GetTileRef(ground, groundGrid, index).Tile.IsEmpty)
                            holes++;
                    }
                }
                Assert.That(holes, Is.Zero, "The ground under and round a wreck was pinned empty.");

                var map = entities.GetComponent<TransformComponent>(ground).MapID;
                var maps = server.System<SharedMapSystem>();
                var flesh = server.ResolveDependency<ITileDefinitionManager>()["WFFloorFlesh"].TileId;
                var wrecks = new List<EntityUid>();
                var query = entities.EntityQueryEnumerator<WFPlanetCityGridComponent, MapGridComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var grid, out var xform))
                {
                    if (xform.MapID != map)
                        continue;
                    wrecks.Add(uid);
                    Assert.Multiple(() =>
                    {
                        Assert.That(entities.HasComponent<ShuttleComponent>(uid), Is.False, "A wreck must not fly.");
                        Assert.That(entities.GetComponent<PhysicsComponent>(uid).BodyType, Is.EqualTo(BodyType.Static));
                        Assert.That(entities.GetComponent<GravityComponent>(uid).Enabled, Is.True, "A wreck needs gravity.");
                        Assert.That(maps.GetAllTiles(uid, grid).Any(tile => tile.Tile.TypeId == flesh), Is.True, "The flesh never crept in.");
                    });
                }
                Assert.That(wrecks, Has.Count.EqualTo(wreckSites.Count), "Every wreck site should have its hull.");

                var fauna = wrecks.Sum(wreck => entities.EntityQuery<MobStateComponent>()
                    .Count(mob => entities.GetComponent<TransformComponent>(mob.Owner).GridUid == wreck));
                Assert.That(fauna, Is.GreaterThanOrEqualTo(wrecks.Count), "The wrecks should have wildlife in them.");
            });
        }
        finally { await PlanetFixture.Teardown(pair, layers); }
        await pair.CleanReturnAsync();
    }
}
