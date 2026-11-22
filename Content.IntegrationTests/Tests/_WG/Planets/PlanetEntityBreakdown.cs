#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Server._WG.Planets;
using Content.Server._WG.Planets.Bounds;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Planets;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._WG.Planets;

/// <summary>
/// Not a check: builds every round-start world as a round would and prints what its entities are, to size what
/// unloading unvisited planet ground could remove. For each world: entities by layer, on the ground or on a separate
/// grid standing on it (a ship, a wreck's hull), and whether they would save or are a mob and what it carries.
/// </summary>
[TestFixture, Explicit]
public sealed class PlanetEntityBreakdown
{
    [Test]
    public async Task EveryRoundStartWorld()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WGPlanetNetworkSystem>();
        var worlds = new List<(string Name, EntityUid Network)>();
        var baseline = 0;

        await server.WaitPost(() =>
        {
            var cfg = server.CfgMan;
            cfg.SetCVar(PlanetCVars.PlanetNetworks, true);
            cfg.SetCVar(PlanetCVars.Bounds, true);
            cfg.SetCVar(PlanetCVars.Radius, 0);
            cfg.SetCVar(PlanetCVars.Preload, true);
            cfg.SetCVar(PlanetCVars.PreloadBudget, 2000f);
            cfg.SetCVar(PlanetCVars.GenerationBudget, 2000f);
            cfg.SetCVar(PlanetCVars.TerrainLoadBudget, 0f);
            cfg.SetCVar(PlanetCVars.FixedCities, true);
            cfg.SetCVar(CavernCVars.Caverns, true);
            cfg.SetCVar(PlanetCVars.SiteStash, true);
            baseline = entMan.EntityCount;

            var i = 0;
            foreach (var surface in proto.EnumeratePrototypes<WGPlanetSurfacePrototype>().Where(s => s.BuildAtRoundStart).OrderBy(s => s.ID))
            {
                var built = networks.BuildNetwork(surface, new Vector2(4000f * ++i, 0f), surface.ID, null);
                Assert.That(built, Is.Not.Null, $"{surface.ID} failed to build.");
                worlds.Add((surface.ID, built!.Value));
            }
        });

        var formed = Stopwatch.StartNew();
        bool AllFormed() => worlds.All(w => entMan.HasComponent<WGPlanetPreloadedComponent>(entMan.GetComponent<WGPlanetNetworkComponent>(w.Network).GroundMap));
        for (var tick = 0; tick < 40000 && !AllFormed(); tick++)
        {
            await server.WaitRunTicks(5);
        }

        Assert.That(AllFormed(), Is.True, "Not every world formed.");
        await server.WaitRunTicks(300);

        var report = new StringBuilder();
        await server.WaitPost(() =>
        {
            report.AppendLine($"{worlds.Count} worlds formed in {formed.Elapsed.TotalSeconds:F0} s. Server: {entMan.EntityCount} entities ({baseline} before the worlds).");

            // Which world each layer map belongs to, and what to call the layer.
            var layerOf = new Dictionary<EntityUid, (int World, string Layer)>();
            for (var w = 0; w < worlds.Count; w++)
            {
                var net = entMan.GetComponent<WGPlanetNetworkComponent>(worlds[w].Network);
                void Name(EntityUid map, string name) => layerOf[map] = (w, name);
                for (var l = 0; l < net.Layers.Count; l++)
                    Name(net.Layers[l], $"layer {l}");
                for (var l = 0; l < net.LowerLayers.Count; l++)
                    Name(net.LowerLayers[l], $"below {l}");
                Name(net.GroundMap, "ground");
                Name(net.OrbitMap, "orbit");
            }

            var stats = worlds.Select(_ => new World()).ToArray();
            var inMob = new Dictionary<EntityUid, bool>();

            bool UnderMob(EntityUid uid)
            {
                if (inMob.TryGetValue(uid, out var known))
                    return known;

                var meta = entMan.GetComponent<MetaDataComponent>(uid);
                var harmless = entMan.HasComponent<AudioComponent>(uid)
                               || entMan.HasComponent<TimedDespawnComponent>(uid) && !entMan.HasComponent<MobStateComponent>(uid);
                var self = meta.EntityPrototype is { MapSavable: false } && !harmless;
                var parent = entMan.GetComponent<TransformComponent>(uid).ParentUid;
                var result = self || parent.IsValid() && !entMan.HasComponent<MapComponent>(parent)
                    && !entMan.HasComponent<MapGridComponent>(parent) && UnderMob(parent);
                inMob[uid] = result;
                return result;
            }

            var all = entMan.AllEntityQueryEnumerator<TransformComponent, MetaDataComponent>();
            while (all.MoveNext(out var uid, out var xform, out var meta))
            {
                if (xform.MapUid is not { } map || !layerOf.TryGetValue(map, out var where))
                    continue;

                var world = stats[where.World];
                world.Total++;
                world.Layers[where.Layer] = world.Layers.GetValueOrDefault(where.Layer) + 1;
                if (meta.EntityPaused)
                    world.Paused++;

                var id = meta.EntityPrototype?.ID ?? "(none)";
                if (uid == map)
                {
                    world.Roots++;
                    continue;
                }

                // A separate grid standing on the layer: a landed or crashed ship, a wreck's hull.
                var onHull = xform.GridUid is { } grid && grid != map;
                if (onHull)
                {
                    world.OnHulls++;
                    world.Hulls[xform.GridUid!.Value] = world.Hulls.GetValueOrDefault(xform.GridUid!.Value) + 1;
                }

                var mob = uid != xform.GridUid && UnderMob(uid);
                if (mob)
                {
                    world.MobTrees++;
                    if (onHull)
                        world.MobTreesOnHulls++;
                    if (entMan.HasComponent<MobStateComponent>(uid))
                        world.Mobs++;
                    world.MobProtos[id] = world.MobProtos.GetValueOrDefault(id) + 1;
                    continue;
                }

                if (xform.Anchored)
                    world.Anchored++;
                else if (xform.ParentUid == map || xform.ParentUid == xform.GridUid)
                    world.Loose++;
                else
                    world.Contained++;

                world.Protos[id] = world.Protos.GetValueOrDefault(id) + 1;
            }

            var sum = new World();
            for (var w = 0; w < worlds.Count; w++)
            {
                var s = stats[w];
                report.AppendLine();
                report.AppendLine($"== {worlds[w].Name}: {s.Total} entities, {s.Paused} paused ==");
                report.AppendLine($"  by layer: {string.Join(", ", s.Layers.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}"))}");
                report.AppendLine($"  would save: {s.Anchored + s.Loose + s.Contained} (anchored {s.Anchored}, loose {s.Loose}, inside something {s.Contained})");
                report.AppendLine($"  would not save: {s.MobTrees} in {s.Mobs} mobs and what they carry (organs, gear, actions)");
                report.AppendLine($"  on separate grids standing on the world: {s.OnHulls} entities on {s.Hulls.Count} grids ({s.MobTreesOnHulls} of them mobs and what they carry); biggest {string.Join(", ", s.Hulls.Values.OrderByDescending(v => v).Take(8))}");
                report.AppendLine($"  top saved prototypes: {string.Join(", ", s.Protos.OrderByDescending(p => p.Value).Take(25).Select(p => $"{p.Key} {p.Value}"))}");
                report.AppendLine($"  top mob-tree prototypes: {string.Join(", ", s.MobProtos.OrderByDescending(p => p.Value).Take(15).Select(p => $"{p.Key} {p.Value}"))}");

                sum.Total += s.Total;
                sum.Paused += s.Paused;
                sum.Anchored += s.Anchored;
                sum.Loose += s.Loose;
                sum.Contained += s.Contained;
                sum.MobTrees += s.MobTrees;
                sum.Mobs += s.Mobs;
                sum.OnHulls += s.OnHulls;
                sum.MobTreesOnHulls += s.MobTreesOnHulls;
                sum.Roots += s.Roots;
            }

            report.AppendLine();
            report.AppendLine($"== All worlds: {sum.Total} entities, {sum.Paused} paused ==");
            report.AppendLine($"  would save: {sum.Anchored + sum.Loose + sum.Contained} (anchored {sum.Anchored}, loose {sum.Loose}, inside something {sum.Contained})");
            report.AppendLine($"  would not save: {sum.MobTrees} in {sum.Mobs} mobs and what they carry");
            report.AppendLine($"  on separate grids: {sum.OnHulls} ({sum.MobTreesOnHulls} of them mobs and what they carry)");
            report.AppendLine($"  layer maps: {sum.Roots}");
        });

        TestContext.Out.WriteLine(report.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "PlanetEntityBreakdown.txt"), report.ToString());
        TestContext.Out.WriteLine("Written to " + System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "PlanetEntityBreakdown.txt"));
        await pair.CleanReturnAsync();
    }

    private sealed class World
    {
        public int Total, Paused, Roots, Anchored, Loose, Contained, MobTrees, Mobs, OnHulls, MobTreesOnHulls;
        public readonly Dictionary<string, int> Layers = new();
        public readonly Dictionary<EntityUid, int> Hulls = new();
        public readonly Dictionary<string, int> Protos = new();
        public readonly Dictionary<string, int> MobProtos = new();
    }
}
