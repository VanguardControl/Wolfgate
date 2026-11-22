#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.ColdStorage;
using Content.Server._WG.Planets;
using Content.Server._WG.Planets.Bounds;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Planets;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.ColdStorage;

/// <summary>
/// A real world is unloaded and loaded back: its layers, hulls and mobs stay, everything else goes and returns
/// where it was, and nothing is left asleep.
/// </summary>
[TestFixture]
[TestOf(typeof(WGPlanetStoreSystem))]
public sealed class PlanetStoreTest
{
    [TestCase("WGSurfaceAerumna")]
    [TestCase("WGSurfaceCarcinoma", Explicit = true)]
    [TestCase("WGSurfaceFervidus", Explicit = true)]
    [TestCase("WGSurfaceMerak", Explicit = true)]
    [TestCase("WGSurfaceThrascias", Explicit = true)]
    public async Task AWorldGoesAndComesBackAsItWas(string surface)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WGPlanetNetworkSystem>();
        var store = server.System<WGPlanetStoreSystem>();
        EntityUid network = default;

        await server.WaitPost(() =>
        {
            var cfg = server.CfgMan;
            cfg.SetCVar(PlanetCVars.PlanetNetworks, true);
            cfg.SetCVar(PlanetCVars.Bounds, true);
            cfg.SetCVar(PlanetCVars.Radius, 0);
            cfg.SetCVar(PlanetCVars.Preload, true);
            cfg.SetCVar(PlanetCVars.FixedCities, true);
            cfg.SetCVar(PlanetCVars.PreloadBudget, 2000f);
            cfg.SetCVar(PlanetCVars.GenerationBudget, 2000f);
            cfg.SetCVar(PlanetCVars.TerrainLoadBudget, 0f);
            cfg.SetCVar(CavernCVars.Caverns, true);
            cfg.SetCVar(PlanetCVars.SiteStash, true);
            cfg.SetCVar(WGPlanetStoreCVars.Idle, 100000f);
            cfg.SetCVar(WGPlanetStoreCVars.BudgetMs, 50f);
            cfg.SetCVar(WGPlanetStoreCVars.Enabled, true);

            var built = networks.BuildNetwork(proto.Index<WGPlanetSurfacePrototype>(surface), new Vector2(4000f, 0f), surface, null);
            Assert.That(built, Is.Not.Null);
            network = built!.Value;
        });

        var net = entMan.GetComponent<WGPlanetNetworkComponent>(network);
        for (var tick = 0; tick < 40000 && !entMan.HasComponent<WGPlanetPreloadedComponent>(net.GroundMap); tick++)
        {
            await server.WaitRunTicks(5);
        }

        Assert.That(entMan.HasComponent<WGPlanetPreloadedComponent>(net.GroundMap), Is.True, "The world never formed.");
        await server.WaitRunTicks(120);

        var layers = new HashSet<EntityUid>(net.Layers);
        layers.UnionWith(net.LowerLayers);
        layers.Add(net.GroundMap);
        layers.Add(net.OrbitMap);

        Dictionary<string, int> before = new();
        List<EntityUid> mobs = new(), hulls = new();
        var total = 0;
        await server.WaitPost(() =>
        {
            before = Census(entMan, layers, out total);
            var all = entMan.AllEntityQueryEnumerator<TransformComponent>();
            while (all.MoveNext(out var uid, out var xform))
            {
                if (xform.MapUid is not { } map || !layers.Contains(map))
                    continue;

                // A few mobs save like furniture (a tumbleweed, a turret) and go into the file with it.
                if (entMan.HasComponent<MobStateComponent>(uid)
                    && entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype is { MapSavable: false })
                    mobs.Add(uid);
                else if (uid != map && entMan.HasComponent<MapGridComponent>(uid))
                    hulls.Add(uid);
            }

            Assert.That(mobs, Is.Not.Empty, "Precondition: the world has mobs.");
            Assert.That(hulls, Is.Not.Empty, "Precondition: the world has hulls standing on it.");
            Assert.That(store.Occupied(network, net, out var who), Is.False, $"Precondition: the world is empty, but {who}.");
            Assert.That(store.TryUnload(network, out var why), Is.True, why);
        });

        try
        {
            for (var tick = 0; tick < 20000 && store.Planets[network].State != WGPlanetStoreState.Unloaded; tick++)
            {
                await server.WaitRunTicks(5);
            }

            var planet = store.Planets[network];
            await server.WaitAssertion(() => Assert.Multiple(() =>
            {
                Assert.That(planet.State, Is.EqualTo(WGPlanetStoreState.Unloaded), planet.LastError);
                Assert.That(planet.StoredEntities, Is.GreaterThan(total / 2), "Less than half the world was unloaded.");
                Census(entMan, layers, out var left);
                Assert.That(left, Is.EqualTo(total - planet.StoredEntities).Within(50), "What is left is not what stayed.");
                Assert.That(layers.All(entMan.EntityExists), Is.True, "A layer was deleted.");
                Assert.That(hulls.All(entMan.EntityExists), Is.True, "A hull standing on the world was deleted.");
                Assert.That(mobs.All(entMan.EntityExists), Is.True, "A mob was deleted.");
                Assert.That(mobs.All(m => entMan.GetComponent<MetaDataComponent>(m).EntityPaused), Is.True, "A mob is awake on an empty world.");
                TestContext.Out.WriteLine($"Unloaded: {total} -> {left} entities; stored {planet.StoredEntities}, kept {planet.KeptEntities}; save {planet.SaveMs:0} ms, delete {planet.DeleteMs:0} ms, {planet.Bytes} bytes.");
            }));

            await server.WaitRunTicks(60);
            await server.WaitPost(() => store.Load(network));
            for (var tick = 0; tick < 20000 && planet.State != WGPlanetStoreState.Loaded; tick++)
            {
                await server.WaitRunTicks(5);
            }

            await server.WaitRunTicks(60);
            await server.WaitAssertion(() => Assert.Multiple(() =>
            {
                Assert.That(planet.State, Is.EqualTo(WGPlanetStoreState.Loaded), planet.LastError);
                Assert.That(store.StoredEntities, Is.Zero);
                var after = Census(entMan, layers, out var back);
                var diff = before.Keys.Union(after.Keys)
                    .Where(k => before.GetValueOrDefault(k) != after.GetValueOrDefault(k))
                    .Select(k => $"{k} {before.GetValueOrDefault(k)}->{after.GetValueOrDefault(k)}")
                    .Take(40).ToList();
                Assert.That(diff, Is.Empty, "Things came back elsewhere, in other numbers or not at all.");
                Assert.That(mobs.All(entMan.EntityExists), Is.True);
                Assert.That(hulls.All(entMan.EntityExists), Is.True);
                Assert.That(entMan.Count<WGKeptPipeGasComponent>() + entMan.Count<WGKeptWiresComponent>(), Is.Zero, "Kept state was not given back.");
                TestContext.Out.WriteLine($"Loaded: {back} entities; load {planet.LoadMs:0} ms over {planet.LoadTicks} ticks, slowest {planet.WorstMs:0.#} ms.");
            }));
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WGPlanetStoreCVars.Enabled, false));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Counts what would save on a world by prototype, what it stands on, where and whether it is anchored: the same
    /// before and after only if everything came back in place.
    /// </summary>
    private static Dictionary<string, int> Census(IEntityManager entMan, HashSet<EntityUid> layers, out int total)
    {
        var counts = new Dictionary<string, int>();
        total = 0;
        var all = entMan.AllEntityQueryEnumerator<TransformComponent, MetaDataComponent>();
        while (all.MoveNext(out var uid, out var xform, out var meta))
        {
            if (xform.MapUid is not { } map || !layers.Contains(map))
                continue;

            total++;
            if (meta.EntityPrototype is not { MapSavable: true } prototype || uid == map)
                continue;

            // A shield bubble is raised again by its emitter, not put back.
            if (prototype.ID == "ShipShield")
                continue;

            // Only what stands straight on a layer or a hull has a place that must match; the rest is counted.
            var parent = xform.ParentUid;
            var placed = parent == map || entMan.HasComponent<MapGridComponent>(parent);
            // An anchored thing must be on its tile; a loose one may have settled a hair, so only what it is on counts.
            var where = !placed
                ? "inside"
                : xform.Anchored
                    ? $"{parent}@{MathF.Floor(xform.LocalPosition.X)},{MathF.Floor(xform.LocalPosition.Y)} anchored"
                    : $"{parent} loose";
            var key = $"{prototype.ID} {where}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts;
    }
}
