#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.Shipyard;
using Content.Server.Atmos.Components;
using Content.Server.Decals;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Server.NPC.HTN;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Storage.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Light.Components;
using Content.Shared.NodeContainer;
using Content.Shared.Power.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Wires;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.ColdStorage;

/// <summary>
/// Measures what a grid loses when it is saved, deleted and loaded back, for the Stored tier of Cold Storage. Prints a
/// report to the test output; it fails only when the harness itself breaks.
/// </summary>
[TestFixture]
[Explicit]
public sealed class GridRoundTripAudit
{
    private const float SettleSeconds = 3f;
    private const float GapSeconds = 5f;
    private const float AfterSeconds = 3f;
    private const int TopRows = 30;

    private static readonly string[] RepresentativeGrids =
    {
        "vessel:Guppy",
        "vessel:Arribane",
        "vessel:Pelican",
        "vessel:Leviathan",
        "/Maps/Salvage/medium-ruined-emergency-shuttle.yml",
        "/Maps/Salvage/ruin-cargo-salvage.yml",
    };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static SerializationOptions SaveOptions => SerializationOptions.Default with
    {
        MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
        LogAutoInclude = null,
        Category = FileCategory.Grid,
        ErrorOnOrphan = false,
    };

    /// <summary>Full audit of a few grids: mutate, save raw and with the resale pipeline, reload, diff.</summary>
    [Test]
    public async Task Representative()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var ctx = new Ctx(pair);
        var report = new StringBuilder();
        var total = new Leg("all grids");

        using (ctx.CaptureLogs())
        {
            foreach (var source in RepresentativeGrids)
            {
                var section = new StringBuilder();
                try
                {
                    await AuditGrid(ctx, source, section, total, false);
                }
                catch (Exception e)
                {
                    section.AppendLine($"!! harness error on {source}: {e}");
                }

                report.Append(section);
            }
        }

        report.AppendLine("################ ALL GRIDS, leg A (raw save) and B (resale pipeline) combined ################");
        report.AppendLine(total.Render(TopRows * 2));
        report.AppendLine("################ ALL GRIDS, copies after settling (A and B combined; compare with the at-load totals above) ################");
        report.AppendLine(ctx.Settled.Render(TopRows * 2));
        Publish("Representative", report);
        try
        {
            await pair.CleanReturnAsync();
        }
        catch (Exception e)
        {
            TestContext.Out.WriteLine($"!! pair did not return cleanly (a failed load left the world inconsistent): {e.Message}");
        }
    }

    /// <summary>
    /// The same audit with a dead human and a live carp aboard, on its own server: a mob in the save can break the load
    /// and leave the world inconsistent, so the pair is not reused.
    /// </summary>
    [Test]
    public async Task MobsAboard()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var ctx = new Ctx(pair);
        var section = new StringBuilder();
        using (ctx.CaptureLogs())
        {
            try
            {
                await AuditGrid(ctx, "vessel:Guppy", section, new Leg("mobs"), true);
            }
            catch (Exception e)
            {
                section.AppendLine($"!! harness error: {e}");
            }
        }

        Publish("MobsAboard", section);
        try
        {
            await pair.CleanReturnAsync();
        }
        catch (Exception e)
        {
            TestContext.Out.WriteLine($"!! pair did not return cleanly (world left inconsistent): {e.Message}");
        }
    }

    /// <summary>Lighter pass over every shipyard vessel: save, reload, entity counts, failures and sizes.</summary>
    [Test]
    public async Task EveryVessel()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var ctx = new Ctx(pair);
        var vessels = ctx.Protos.EnumeratePrototypes<VesselPrototype>().OrderBy(v => v.ID).ToList();
        var rows = new List<(string Id, int Live, int Loaded, int Bytes, int Gzip, double Save, double Load, bool RawOk)>();
        var missing = new Dictionary<string, Tally>();
        var rawFailures = new Dictionary<string, Tally>();
        var loadFailures = new List<string>();
        var watch = Stopwatch.StartNew();

        using (ctx.CaptureLogs())
        {
            foreach (var vessel in vessels)
            {
                EntityUid map = default;
                EntityUid grid = default;
                ctx.Phase = $"{vessel.ID} load file";
                await ctx.Pair.Server.WaitPost(() =>
                {
                    map = ctx.Maps.CreateMap(out var mapId);
                    if (ctx.Loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var g))
                        grid = g.Value.Owner;
                });

                if (!grid.IsValid())
                {
                    loadFailures.Add($"{vessel.ID}: file did not load ({vessel.ShuttlePath})");
                    await ctx.Pair.Server.WaitPost(() => ctx.E.DeleteEntity(map));
                    continue;
                }

                await pair.RunTicksSync(5);

                ctx.Phase = $"{vessel.ID} save";
                await ctx.Pair.Server.WaitPost(() =>
                {
                    try
                    {
                        var before = CountByProto(ctx, grid);
                        var live = before.Values.Sum();
                        var sw = Stopwatch.StartNew();
                        var rawOk = true;
                        MappingDataNode node;
                        try
                        {
                            node = ctx.Loader.SerializeEntitiesRecursive(new HashSet<EntityUid> { grid }, SaveOptions).Node;
                        }
                        catch (Exception e)
                        {
                            rawOk = false;
                            var culprits = FindUnsavable(ctx, grid);
                            foreach (var (comp, proto, msg) in culprits)
                                Bump(rawFailures, comp, $"{vessel.ID}: {proto}: {msg}");
                            if (culprits.Count == 0)
                                Bump(rawFailures, "(unattributed)", $"{vessel.ID}: {e.Message}");

                            ctx.Yard.WGPruneDeadKeys(grid);
                            ctx.Yard.WGStripUnsavable(grid, SaveOptions);
                            node = ctx.Loader.SerializeEntitiesRecursive(new HashSet<EntityUid> { grid }, SaveOptions).Node;
                        }

                        var text = ctx.Yard.WGWriteCopy(node, vessel.ID) ?? throw new Exception("YAML write failed");
                        var save = sw.Elapsed.TotalMilliseconds;
                        ctx.E.DeleteEntity(grid);
                        ctx.E.DeleteEntity(map);

                        var paused = ctx.Maps.CreateMap(out var pausedId);
                        ctx.Maps.SetPaused(pausedId, true);
                        sw.Restart();
                        if (!ctx.Loader.TryLoadGrid(pausedId, new StringReader(text), vessel.ID, out var loaded))
                        {
                            loadFailures.Add($"{vessel.ID}: saved copy did not load");
                            ctx.E.DeleteEntity(paused);
                            return;
                        }

                        var load = sw.Elapsed.TotalMilliseconds;
                        var after = CountByProto(ctx, loaded.Value.Owner);
                        foreach (var (proto, n) in before)
                        {
                            var lost = n - after.GetValueOrDefault(proto);
                            if (lost > 0)
                                Bump(missing, proto, $"{vessel.ID} -{lost}", lost);
                        }

                        var bytes = Encoding.UTF8.GetByteCount(text);
                        rows.Add((vessel.ID, live, after.Values.Sum(), bytes, Gzip(text), (double) save, (double) load, rawOk));
                        ctx.E.DeleteEntity(paused);
                    }
                    catch (Exception e)
                    {
                        loadFailures.Add($"{vessel.ID}: harness error {e.GetType().Name}: {e.Message}");
                        if (ctx.E.EntityExists(map))
                            ctx.E.DeleteEntity(map);
                    }
                });
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"################ EVERY VESSEL: {vessels.Count} prototypes, {rows.Count} round-tripped, {watch.Elapsed.TotalSeconds:0} s ################");
        if (rows.Count > 0)
        {
            var byBytes = rows.Select(r => r.Bytes).OrderBy(b => b).ToList();
            var byGzip = rows.Select(r => r.Gzip).OrderBy(b => b).ToList();
            sb.AppendLine($"raw save (no strip/prune) succeeded: {rows.Count(r => r.RawOk)}/{rows.Count}");
            sb.AppendLine($"entities live total {rows.Sum(r => r.Live)}, after reload {rows.Sum(r => r.Loaded)} ({rows.Count(r => r.Loaded != r.Live)} vessels changed count)");
            sb.AppendLine($"YAML bytes: min {byBytes[0]}, median {byBytes[byBytes.Count / 2]}, p90 {byBytes[(int) (byBytes.Count * 0.9)]}, max {byBytes[^1]}, sum {byBytes.Sum()}");
            sb.AppendLine($"gzip bytes: min {byGzip[0]}, median {byGzip[byGzip.Count / 2]}, p90 {byGzip[(int) (byGzip.Count * 0.9)]}, max {byGzip[^1]}, sum {byGzip.Sum()}");
            sb.AppendLine($"save ms: median {Median(rows.Select(r => r.Save))}, max {rows.Max(r => r.Save):0}; load ms: median {Median(rows.Select(r => r.Load))}, max {rows.Max(r => r.Load):0}");
            sb.AppendLine("largest 10:");
            foreach (var r in rows.OrderByDescending(r => r.Live).Take(10))
                sb.AppendLine($"  {r.Id}: {r.Live} ents -> {r.Loaded}, {r.Bytes / 1024} KiB yaml, {r.Gzip / 1024} KiB gz, save {r.Save:0} ms, load {r.Load:0} ms, raw {(r.RawOk ? "ok" : "FAILED")}");
        }

        sb.AppendLine("raw save failures by component (example):");
        sb.Append(RenderTallies(rawFailures, TopRows));
        sb.AppendLine("entities lost on reload by prototype, summed over all vessels (example):");
        sb.Append(RenderTallies(missing, TopRows));
        sb.AppendLine("load failures / harness errors:");
        foreach (var f in loadFailures)
            sb.AppendLine("  " + f);
        sb.AppendLine("warnings/errors logged:");
        sb.Append(ctx.RenderLogs(TopRows));
        Publish("EveryVessel", sb);

        await pair.CleanReturnAsync();
    }

    /// <summary>Writes the report to the test output and to a file beside the test binaries, since passing tests' output is hidden.</summary>
    private static void Publish(string name, StringBuilder report)
    {
        TestContext.Out.WriteLine(report.ToString());
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"GridRoundTripAudit.{name}.txt");
        File.WriteAllText(path, report.ToString());
        TestContext.Out.WriteLine($"Report written to {path}");
    }

    #region Audit of one grid

    private async Task AuditGrid(Ctx ctx, string source, StringBuilder sb, Leg total, bool withMobs)
    {
        var pair = ctx.Pair;
        var server = ctx.Pair.Server;
        var path = ResolvePath(ctx, source);
        sb.AppendLine($"==================== {source} ({path}) ====================");
        ctx.ClearLogs();

        EntityUid liveMap = default;
        EntityUid grid = default;
        var cost = new StringBuilder();

        ctx.Phase = "load file";
        await server.WaitPost(() =>
        {
            liveMap = ctx.Maps.CreateMap(out var mapId);
            var sw = Stopwatch.StartNew();
            if (!ctx.Loader.TryLoadGrid(mapId, path, out var g))
                throw new Exception($"Could not load {path}");
            grid = g.Value.Owner;
            cost.AppendLine($"  load from map file (pre-init, runs MapInit): {sw.Elapsed.TotalMilliseconds:0} ms");
        });

        ctx.Phase = "settle";
        await pair.RunTicksSync(ctx.Ticks(SettleSeconds));

        var mutations = new List<Mutation>();
        ctx.Phase = "mutate";
        await server.WaitPost(() => mutations.AddRange(Mutate(ctx, grid, withMobs)));
        await pair.RunTicksSync(ctx.Ticks(1f));

        Snap before = default!;
        Dictionary<string, string> probesBefore = default!;
        string? textA = null;
        string? textB = null;
        var unsavable = new List<(string Comp, string Proto, string Msg)>();
        var stripRemoved = new Dictionary<string, Tally>();
        var pruneRemoved = new Dictionary<string, int>();
        var yamlInfo = new StringBuilder();
        var carried = 0;

        ctx.Phase = "save";
        await server.WaitPost(() =>
        {
            before = TakeSnap(ctx, grid);
            probesBefore = Probes(ctx, grid);
            foreach (var m in mutations)
            {
                m.Key = before.Keys.GetValueOrDefault(m.Uid, "?");
                m.Before = m.Probe(m.Uid);
            }

            sb.AppendLine($"live: {before.Ents.Count} entities, {before.Ents.Count(e => !e.Savable)} with MapSavable=false, {before.Ents.Count(e => e.Contained)} in containers");

            // Leg A: the engine serializer as is.
            ctx.Phase = "save A (raw)";
            var sw = Stopwatch.StartNew();
            try
            {
                var (node, cat) = ctx.Loader.SerializeEntitiesRecursive(new HashSet<EntityUid> { grid }, SaveOptions);
                var serialiseMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                textA = ctx.Yard.WGWriteCopy(node, source);
                var writeMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                ctx.Loader.SerializeEntitiesRecursive(new HashSet<EntityUid> { grid }, SaveOptions);
                cost.AppendLine($"  A save: serialise {serialiseMs:0} ms cold / {sw.Elapsed.TotalMilliseconds:0} ms warm (second run) + write text {writeMs:0} ms, category {cat}");
                DescribeYaml(node, textA, "A", yamlInfo);
            }
            catch (Exception e)
            {
                cost.AppendLine($"  A save FAILED after {sw.Elapsed.TotalMilliseconds:0} ms: {e.GetType().Name}: {e.Message}");
            }

            // Which components throw, found with every grid entity's id reserved first (as a real save does).
            unsavable.AddRange(FindUnsavable(ctx, grid));

            // Leg B: what the resale pipeline does: prune, carry unsavable mobs, strip on failure.
            ctx.Phase = "save B (pipeline)";
            var prunedBefore = PruneCounts(ctx, grid);
            sw.Restart();
            ctx.Yard.WGPruneDeadKeys(grid);
            var pruneMs = sw.Elapsed.TotalMilliseconds;
            var prunedAfter = PruneCounts(ctx, grid);
            foreach (var (k, v) in prunedBefore)
                pruneRemoved[k] = v - prunedAfter.GetValueOrDefault(k);

            var carry = ctx.E.System<UsedShipMarketSystem>().GetUnsavableAboard(grid);
            carried = carry.Count;
            var lifted = new List<EntityPrototype>();
            foreach (var uid in carry)
            {
                if (ctx.E.GetComponent<MetaDataComponent>(uid).EntityPrototype is { MapSavable: false } proto && !lifted.Contains(proto))
                    lifted.Add(proto);
            }

            foreach (var proto in lifted)
                proto.MapSavable = true;

            try
            {
                sw.Restart();
                MappingDataNode nodeB;
                var stripped = false;
                try
                {
                    nodeB = ctx.Loader.SerializeEntitiesRecursive(new HashSet<EntityUid> { grid }, SaveOptions).Node;
                }
                catch (Exception)
                {
                    stripped = true;
                    var compsBefore = CompSets(ctx, grid);
                    ctx.Yard.WGStripUnsavable(grid, SaveOptions);
                    DiffCompSets(compsBefore, CompSets(ctx, grid), stripRemoved);
                    nodeB = ctx.Loader.SerializeEntitiesRecursive(new HashSet<EntityUid> { grid }, SaveOptions).Node;
                }

                var saveMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                textB = ctx.Yard.WGWriteCopy(nodeB, source);
                cost.AppendLine($"  B save: prune {pruneMs:0} ms, serialise {saveMs:0} ms{(stripped ? " (incl. strip + retry)" : "")}, write text {sw.Elapsed.TotalMilliseconds:0} ms, carried {carry.Count} unsavable entities");
                DescribeYaml(nodeB, textB, "B", yamlInfo);
            }
            catch (Exception e)
            {
                cost.AppendLine($"  B save FAILED: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                foreach (var proto in lifted)
                    proto.MapSavable = false;
            }

            ctx.Phase = "delete";
            sw.Restart();
            ctx.E.DeleteEntity(grid);
            cost.AppendLine($"  delete grid in one go: {sw.Elapsed.TotalMilliseconds:0} ms ({before.Ents.Count} entities)");

            // Anything from the hull that outlived its deletion (a mob's parts, for one).
            foreach (var (uid, key) in before.Keys)
            {
                if (ctx.E.EntityExists(uid) && !ctx.E.IsQueuedForDeletion(uid))
                    cost.AppendLine($"  !! outlived the grid's deletion: {key} ({ctx.E.ToPrettyString(uid)}, parent {ctx.E.ToPrettyString(ctx.E.GetComponent<TransformComponent>(uid).ParentUid)})");
            }

            ctx.E.DeleteEntity(liveMap);
        });

        sb.AppendLine("cost:");
        sb.Append(cost);
        sb.Append(yamlInfo);

        sb.AppendLine("1. serializer failures without the strip step (first throwing component per entity, ids reserved):");
        if (unsavable.Count == 0)
            sb.AppendLine("  none");
        foreach (var g in unsavable.GroupBy(u => u.Comp))
            sb.AppendLine($"  {g.Key}: {g.Count()} entities, e.g. {g.First().Proto}: {g.First().Msg}");
        sb.AppendLine("   strip step (only runs when the save throws) removed:");
        sb.Append(stripRemoved.Count == 0 ? "  nothing (save did not throw)\n" : RenderTallies(stripRemoved, TopRows));
        sb.AppendLine("   prune step removed dictionary entries: " +
                      string.Join(", ", pruneRemoved.Select(p => $"{p.Key} {p.Value}")));
        sb.AppendLine($"   resale carry list (MapSavable=false lifted): {carried} entities");
        sb.AppendLine("   warnings/errors logged while loading, mutating and saving:");
        sb.Append(ctx.RenderLogs(TopRows));
        ctx.ClearLogs();

        ctx.Phase = "gap";
        await pair.RunTicksSync(ctx.Ticks(GapSeconds));

        var legA = new Leg("A raw");
        var legB = new Leg("B pipeline");
        var loadA = await LoadAndDiff(ctx, textA, source, before, legA, mutations, false);
        var loadB = await LoadAndDiff(ctx, textB, source, before, legB, mutations, true);

        // MapInit check: the same text loaded onto an initialised, running map.
        var mapInitCheck = "not run";
        var initText = textB ?? textA;
        if (initText != null)
        {
            ctx.Phase = "MapInit check load";
            await server.WaitPost(() =>
            {
                var map = ctx.Maps.CreateMap(out var mapId);
                Entity<MapGridComponent>? g = null;
                bool loadedOk;
                try
                {
                    loadedOk = ctx.Loader.TryLoadGrid(mapId, new StringReader(initText), source, out g);
                }
                catch (Exception e)
                {
                    mapInitCheck = $"load THREW {e.GetType().Name}: {Trim(e.Message)}";
                    return;
                }

                if (loadedOk && g != null)
                {
                    var ents = AllOnGrid(ctx, g.Value.Owner);
                    var notInit = ents.Count(u => ctx.E.GetComponent<MetaDataComponent>(u).EntityLifeStage < EntityLifeStage.MapInitialized);
                    var probes = Probes(ctx, g.Value.Owner);
                    var pausedCount = (textB != null ? loadB : loadA)?.Count ?? -1;
                    mapInitCheck = $"loaded onto an initialised unpaused map: {ents.Count} entities (paused-map load had {pausedCount}), {notInit} not map-initialised; solution volume {probes.GetValueOrDefault("solution.volume")} vs before {probesBefore.GetValueOrDefault("solution.volume")}";
                }
                else
                {
                    mapInitCheck = "load failed";
                }

                ctx.E.DeleteEntity(map);
            });
        }

        // Settle both copies, then read the probes again.
        ctx.Phase = "settle copies";
        await server.WaitPost(() =>
        {
            if (loadB?.Grid is { } gb && ctx.E.EntityExists(gb))
                ctx.E.System<UsedShipReinitSystem>().ReinitLoadedShip(gb);
            foreach (var l in new[] { loadA, loadB })
            {
                if (l?.Map is { } m && ctx.E.EntityExists(m))
                    ctx.Maps.SetPaused(m, false);
            }
        });
        await pair.RunTicksSync(ctx.Ticks(AfterSeconds));

        Dictionary<string, string>? settledA = null;
        Dictionary<string, string>? settledB = null;
        var stripOnCopy = new Dictionary<string, Tally>();
        var legAS = new Leg($"A settled {AfterSeconds}s after unpause (includes {AfterSeconds}s of simulation)");
        var legBS = new Leg($"B settled {AfterSeconds}s after reinit + unpause (includes {AfterSeconds}s of simulation)");
        await server.WaitPost(() =>
        {
            if (loadA?.Grid is { } ga && ctx.E.EntityExists(ga))
            {
                settledA = Probes(ctx, ga);
                Diff(before, TakeSnap(ctx, ga), legAS);
                foreach (var m in mutations)
                    m.SettledA = ProbeAfter(ctx, m, loadA);

                // What an unconditional strip would take off, measured on the throwaway copy.
                var compsBefore = CompSets(ctx, ga);
                ctx.Yard.WGStripUnsavable(ga, SaveOptions);
                DiffCompSets(compsBefore, CompSets(ctx, ga), stripOnCopy);
            }

            if (loadB?.Grid is { } gb && ctx.E.EntityExists(gb))
            {
                settledB = Probes(ctx, gb);
                Diff(before, TakeSnap(ctx, gb), legBS);
                foreach (var m in mutations)
                    m.SettledB = ProbeAfter(ctx, m, loadB);
            }

            foreach (var l in new[] { loadA, loadB })
            {
                if (l?.Map is { } m && ctx.E.EntityExists(m))
                    ctx.E.DeleteEntity(m);
            }
        });

        sb.AppendLine($"   WGStripUnsavable run unconditionally on copy A would remove:");
        sb.Append(stripOnCopy.Count == 0 ? "  nothing\n" : RenderTallies(stripOnCopy, TopRows));

        sb.AppendLine("6. MapInit:");
        sb.AppendLine($"  A: {loadA?.MapInitLine ?? "not loaded"}");
        sb.AppendLine($"  B: {loadB?.MapInitLine ?? "not loaded"}");
        sb.AppendLine($"  {mapInitCheck}");

        sb.AppendLine("7. cost of loads:");
        sb.AppendLine($"  A: {loadA?.CostLine ?? "not loaded"}");
        sb.AppendLine($"  B: {loadB?.CostLine ?? "not loaded"}");

        sb.AppendLine("4a. mutations (live -> A at load / A settled -> B at load / B settled after reinit):");
        foreach (var m in mutations)
            sb.AppendLine($"  {m.Label} [{m.Key}]: {m.Before} -> {m.LoadA ?? "-"} / {m.SettledA ?? "-"} -> {m.LoadB ?? "-"} / {m.SettledB ?? "-"}");

        sb.AppendLine("4b. grid probes: before | A load | A settled | B load | B settled(reinit)");
        var keys = probesBefore.Keys.Union(loadA?.Probes.Keys ?? Enumerable.Empty<string>()).OrderBy(k => k);
        foreach (var k in keys)
        {
            string P(Dictionary<string, string>? d) => d?.GetValueOrDefault(k) ?? "-";
            var row = $"{P(probesBefore)} | {P(loadA?.Probes)} | {P(settledA)} | {P(loadB?.Probes)} | {P(settledB)}";
            sb.AppendLine($"  {k}: {row}");
        }

        foreach (var leg in new[] { legA, legB })
        {
            sb.AppendLine(leg.Render(TopRows));
            total.Absorb(leg);
        }

        foreach (var leg in new[] { legAS, legBS })
        {
            sb.AppendLine(leg.Render(TopRows));
            ctx.Settled.Absorb(leg);
        }

        sb.AppendLine("   warnings/errors logged while loading and settling the copies:");
        sb.Append(ctx.RenderLogs(TopRows));
    }

    private sealed class Loaded
    {
        public EntityUid Map;
        public EntityUid Grid;
        public int Count;
        public Snap Snap = default!;
        public Dictionary<string, string> Probes = new();
        public string MapInitLine = "";
        public string CostLine = "";
    }

    private async Task<Loaded?> LoadAndDiff(Ctx ctx, string? text, string source, Snap before, Leg leg, List<Mutation> mutations, bool isB)
    {
        if (text == null)
        {
            leg.Notes.Add("not loaded: save failed");
            return null;
        }

        Loaded? result = null;
        ctx.Phase = $"load {leg.Name}";
        await ctx.Pair.Server.WaitPost(() =>
        {
            var map = ctx.Maps.CreateMap(out var mapId);
            ctx.Maps.SetPaused(mapId, true);
            var sw = Stopwatch.StartNew();
            Entity<MapGridComponent>? g;
            try
            {
                if (!ctx.Loader.TryLoadGrid(mapId, new StringReader(text), source, out g))
                {
                    leg.Notes.Add("load FAILED (returned false)");
                    ctx.E.DeleteEntity(map);
                    return;
                }
            }
            catch (Exception e)
            {
                leg.Notes.Add($"load THREW {e.GetType().Name}: {Trim(e.Message)}");
                return;
            }

            var loadMs = sw.Elapsed.TotalMilliseconds;
            var grid = g!.Value.Owner;
            var after = TakeSnap(ctx, grid);
            var notInit = after.Ents.Count(e => e.LifeStage < EntityLifeStage.MapInitialized);
            var paused = after.Ents.Count(e => e.Paused);
            result = new Loaded
            {
                Map = map,
                Grid = grid,
                Count = after.Ents.Count,
                Snap = after,
                Probes = Probes(ctx, grid),
                MapInitLine = $"{notInit} of {after.Ents.Count} loaded entities not map-initialised; {paused} paused (target map paused)",
                CostLine = $"load onto paused map {loadMs:0} ms for {after.Ents.Count} entities, {Encoding.UTF8.GetByteCount(text)} bytes",
            };

            Diff(before, after, leg);
            foreach (var m in mutations)
            {
                if (isB)
                    m.LoadB = ProbeAfter(ctx, m, result);
                else
                    m.LoadA = ProbeAfter(ctx, m, result);
            }
        });

        return result;
    }

    private static string? ProbeAfter(Ctx ctx, Mutation m, Loaded? loaded)
    {
        if (loaded == null)
            return null;
        if (!m.Uid.IsValid())
            return "-";
        var uid = loaded.Snap.Keys.FirstOrDefault(kv => kv.Value == m.Key).Key;
        if (!uid.IsValid() || !ctx.E.EntityExists(uid))
            return "MISSING";
        try
        {
            return m.Probe(uid);
        }
        catch (Exception e)
        {
            return $"probe threw {e.GetType().Name}";
        }
    }

    #endregion

    #region Mutations

    private sealed class Mutation
    {
        public string Label = "";
        public EntityUid Uid;
        public Func<EntityUid, string> Probe = _ => "";
        public string Key = "";
        public string Before = "";
        public string? LoadA;
        public string? LoadB;
        public string? SettledA;
        public string? SettledB;
    }

    private static List<Mutation> Mutate(Ctx ctx, EntityUid grid, bool withMobs)
    {
        var e = ctx.E;
        var list = new List<Mutation>();
        var ents = AllOnGrid(ctx, grid);
        var used = new HashSet<EntityUid>();

        EntityUid? Pick(Func<EntityUid, bool> pred)
        {
            foreach (var uid in ents)
            {
                if (used.Contains(uid) || !e.EntityExists(uid))
                    continue;
                try
                {
                    if (!pred(uid))
                        continue;
                }
                catch
                {
                    continue;
                }

                used.Add(uid);
                return uid;
            }

            return null;
        }

        void Try(string label, Func<EntityUid, bool> pred, Action<EntityUid> act, Func<EntityUid, string> probe)
        {
            var uid = Pick(pred);
            if (uid == null)
            {
                list.Add(new Mutation { Label = label + " (no candidate)", Uid = EntityUid.Invalid, Probe = _ => "-" });
                return;
            }

            try
            {
                act(uid.Value);
            }
            catch (Exception ex)
            {
                label += $" (threw {ex.GetType().Name})";
            }

            list.Add(new Mutation { Label = label, Uid = uid.Value, Probe = probe });
        }

        var blunt = ctx.Protos.Index<DamageTypePrototype>("Blunt");
        var anchorSpot = ents.FirstOrDefault(u => e.GetComponent<TransformComponent>(u).Anchored);
        var spot = anchorSpot.IsValid()
            ? new EntityCoordinates(grid, e.GetComponent<TransformComponent>(anchorSpot).LocalPosition)
            : new EntityCoordinates(grid, Vector2.Zero);

        Try("battery drained to 37%",
            u => e.TryGetComponent<BatteryComponent>(u, out var b) && b.MaxCharge > 0 && e.HasComponent<PowerNetworkBatteryComponent>(u),
            u =>
            {
                var b = e.GetComponent<BatteryComponent>(u);
                e.System<BatterySystem>().SetCharge(u, b.MaxCharge * 0.37f, b);
            },
            u => e.TryGetComponent<BatteryComponent>(u, out var b) ? $"{b.CurrentCharge / Math.Max(1f, b.MaxCharge):P0}" : "no battery");

        Try("APC breaker off",
            u => e.TryGetComponent<ApcComponent>(u, out var a) && a.MainBreakerEnabled,
            u => e.System<ApcSystem>().ApcToggleBreaker(u),
            u => e.TryGetComponent<ApcComponent>(u, out var a) ? (a.MainBreakerEnabled ? "on" : "off") : "no apc");

        Try("door opened",
            u => e.TryGetComponent<DoorComponent>(u, out var d) && d.State == DoorState.Closed
                 && !(e.TryGetComponent<DoorBoltComponent>(u, out var b) && b.BoltsDown),
            u => e.System<SharedDoorSystem>().StartOpening(u),
            u => e.TryGetComponent<DoorComponent>(u, out var d) ? StateName(d.State) : "no door");

        Try("door bolted",
            u => e.TryGetComponent<DoorComponent>(u, out var d) && d.State == DoorState.Closed
                 && e.TryGetComponent<DoorBoltComponent>(u, out var b) && !b.BoltsDown,
            u => e.System<SharedDoorSystem>().SetBoltsDown((u, e.GetComponent<DoorBoltComponent>(u)), true),
            u => e.TryGetComponent<DoorBoltComponent>(u, out var b) ? (b.BoltsDown ? "bolted" : "unbolted") : "no bolts");

        Try("door welded",
            u => e.TryGetComponent<DoorComponent>(u, out var d) && d.State == DoorState.Closed
                 && e.TryGetComponent<WeldableComponent>(u, out var w) && !w.IsWelded,
            u => e.System<WeldableSystem>().SetWeldedState(u, true),
            u => e.TryGetComponent<WeldableComponent>(u, out var w) ? (w.IsWelded ? "welded" : "not welded") : "no weldable");

        Try("crowbar put in a locker",
            u => e.HasComponent<EntityStorageComponent>(u) && e.GetComponent<TransformComponent>(u).Anchored == false,
            u =>
            {
                var item = e.SpawnEntity("Crowbar", e.GetComponent<TransformComponent>(u).Coordinates);
                if (!e.System<SharedEntityStorageSystem>().Insert(item, u))
                    throw new Exception("insert refused");
            },
            u => ContainerLine(ctx, u));

        Try("wall damaged 25 blunt",
            u => e.HasComponent<DamageableComponent>(u)
                 && (e.GetComponent<MetaDataComponent>(u).EntityPrototype?.ID.StartsWith("Wall") ?? false),
            u => e.System<DamageableSystem>().TryChangeDamage(u, new DamageSpecifier(blunt, FixedPoint2.New(25)), true),
            u => e.TryGetComponent<DamageableComponent>(u, out var d) ? $"dmg {d.TotalDamage}" : "no damageable");

        Try("5u water added to a solution",
            u => e.TryGetComponent<SolutionContainerManagerComponent>(u, out var mgr)
                 && TryCompNamed(ctx, u, "RefillableSolution", out _)
                 && e.System<SharedSolutionContainerSystem>().EnumerateSolutions((u, mgr)).Any(s => s.Solution.Comp.Solution.AvailableVolume >= 5),
            u =>
            {
                var sol = e.System<SharedSolutionContainerSystem>().EnumerateSolutions((u, e.GetComponent<SolutionContainerManagerComponent>(u)))
                    .First(s => s.Solution.Comp.Solution.AvailableVolume >= 5);
                e.System<SharedSolutionContainerSystem>().TryAddReagent(sol.Solution, "Water", FixedPoint2.New(5));
            },
            u => SolutionLine(ctx, u));

        Try("thruster switched off",
            u => e.TryGetComponent<ThrusterComponent>(u, out var t) && t.Enabled
                 && !(e.GetComponent<MetaDataComponent>(u).EntityPrototype?.ID.Contains("Gyroscope") ?? false),
            u =>
            {
                var t = e.GetComponent<ThrusterComponent>(u);
                SetMember(t, nameof(ThrusterComponent.Enabled), false);
                e.System<ThrusterSystem>().DisableThruster(u, t, e.GetComponent<TransformComponent>(u));
            },
            u => e.TryGetComponent<ThrusterComponent>(u, out var t) ? (t.Enabled ? "enabled" : "disabled") + (t.IsOn ? ",on" : ",off") : "no thruster");

        Try("stack set to 1",
            u => e.TryGetComponent<StackComponent>(u, out var s) && s.Count > 1,
            u => e.System<SharedStackSystem>().SetCount(u, 1),
            u => e.TryGetComponent<StackComponent>(u, out var s) ? $"count {s.Count}" : "no stack");

        Try("wires panel opened",
            u => e.TryGetComponent<WiresPanelComponent>(u, out var w) && !w.Open,
            u => e.System<SharedWiresSystem>().TogglePanel(u, e.GetComponent<WiresPanelComponent>(u), true),
            u => e.TryGetComponent<WiresPanelComponent>(u, out var w) ? (w.Open ? "open" : "closed") : "no panel");

        // Grid-level mutations are probed on the grid itself.
        var decal = ctx.Protos.EnumeratePrototypes<DecalPrototype>().First().ID;
        var decalOk = e.System<DecalSystem>().TryAddDecal(decal, spot, out _);
        list.Add(new Mutation { Label = $"decal {decal} placed ({(decalOk ? "ok" : "refused")})", Uid = grid, Probe = u => $"decals {DecalCount(ctx, u)}" });

        if (e.TryGetComponent<GridAtmosphereComponent>(grid, out var atmos))
        {
            var tile = atmos.Tiles.Values.FirstOrDefault(t => t.Air is { } air && air.Volume > 0 && !air.Immutable);
            tile?.Air!.AdjustMoles(Gas.Plasma, 3f);
            list.Add(new Mutation { Label = $"3 mol plasma added to a tile ({(tile != null ? "ok" : "no tile air")})", Uid = grid, Probe = u => $"tile plasma {TilePlasma(ctx, u):0.##}" });
        }

        IPipeNet? net = null;
        foreach (var uid in ents)
        {
            if (!e.TryGetComponent<NodeContainerComponent>(uid, out var nodes))
                continue;
            net = nodes.Nodes.Values.OfType<PipeNode>().Select(n => n.NodeGroup).OfType<IPipeNet>().FirstOrDefault(n => n.Air.Volume > 0);
            if (net != null)
                break;
        }

        net?.Air.AdjustMoles(Gas.Nitrogen, 40f);
        list.Add(new Mutation { Label = $"40 mol N2 added to a pipe net ({(net != null ? "ok" : "no pipe net")})", Uid = grid, Probe = u => $"pipe moles {PipeMoles(ctx, u, out _):0.#}" });

        if (ctx.Factory.TryGetRegistration("ShuttleDeed", out var deedReg) && !e.HasComponent(grid, deedReg.Type))
        {
            var deed = (Component) ctx.Factory.GetComponent(deedReg.Type);
            SetMember(deed, "ShuttleUid", (EntityUid?) grid);
            SetMember(deed, "ShuttleName", "Audit");
            SetMember(deed, "ShuttleOwner", "Audit Owner");
            e.AddComponent(grid, deed);
            list.Add(new Mutation
            {
                Label = "ShuttleDeed added to the grid",
                Uid = grid,
                Probe = u => e.TryGetComponent(u, deedReg.Type, out var d)
                    ? $"deed owner {GetMember(d, "ShuttleOwner")}, uid {(GetMember(d, "ShuttleUid") is EntityUid du && du == u ? "self" : GetMember(d, "ShuttleUid")?.ToString())}"
                    : "no deed",
            });
        }

        if (!withMobs)
            return list;

        // A salvage corpse rather than a killed MobHuman: damaging a fresh human to death crashed BarotraumaSystem
        // (Wolfmed damage routing) on a later tick, which has nothing to do with saving.
        var body = e.SpawnEntity("SalvageHumanCorpse", spot);
        list.Add(new Mutation { Label = "SalvageHumanCorpse placed", Uid = body, Probe = u => $"{DamageLine(ctx, u)}, children {e.GetComponent<TransformComponent>(u).ChildCount}" });

        var carp = e.SpawnEntity("MobCarp", spot);
        list.Add(new Mutation { Label = "live MobCarp (HTN NPC) placed", Uid = carp, Probe = u => $"htn {e.HasComponent<HTNComponent>(u)}, {DamageLine(ctx, u)}" });

        return list;
    }

    private static string StateName(DoorState state)
    {
        return state.ToString();
    }

    private static string DamageLine(Ctx ctx, EntityUid uid)
    {
        return ctx.E.TryGetComponent<DamageableComponent>(uid, out var d) ? $"dmg {d.TotalDamage}" : "no damageable";
    }

    private static string ContainerLine(Ctx ctx, EntityUid uid)
    {
        if (!ctx.E.TryGetComponent<ContainerManagerComponent>(uid, out var cm))
            return "no containers";
        return string.Join(";", cm.Containers.Select(c => $"{c.Key}:{string.Join(",", c.Value.ContainedEntities.Select(x => ctx.E.GetComponent<MetaDataComponent>(x).EntityPrototype?.ID))}"));
    }

    private static string SolutionLine(Ctx ctx, EntityUid uid)
    {
        if (!ctx.E.TryGetComponent<SolutionContainerManagerComponent>(uid, out var mgr))
            return "no solutions";
        return string.Join(";", ctx.E.System<SharedSolutionContainerSystem>().EnumerateSolutions((uid, mgr))
            .Select(s => $"{s.Name}={s.Solution.Comp.Solution.Volume}"));
    }

    #endregion

    #region Probes

    private static Dictionary<string, string> Probes(Ctx ctx, EntityUid grid)
    {
        var e = ctx.E;
        var ents = AllOnGrid(ctx, grid);
        var p = new Dictionary<string, string>();
        int anchored = 0, contained = 0, batteries = 0, breakersOff = 0, doorsOpen = 0, doorsClosed = 0, doorsOther = 0,
            bolted = 0, welded = 0, lightsOn = 0, lightsOff = 0, thrustersOn = 0, thrustersOff = 0, solutions = 0,
            damaged = 0, stackTotal = 0, thrustersFiring = 0, wiresEmpty = 0, noAddress = 0, links = 0, deviceListDevices = 0, htn = 0, docked = 0, panelsOpen = 0, notInit = 0, paused = 0;
        float charge = 0, maxCharge = 0, compGas = 0;
        var solVolume = FixedPoint2.Zero;
        var damage = FixedPoint2.Zero;

        foreach (var uid in ents)
        {
            var meta = e.GetComponent<MetaDataComponent>(uid);
            var xform = e.GetComponent<TransformComponent>(uid);
            if (xform.Anchored)
                anchored++;
            if ((meta.Flags & MetaDataFlags.InContainer) != 0)
                contained++;
            if (meta.EntityLifeStage < EntityLifeStage.MapInitialized)
                notInit++;
            if (meta.EntityPaused)
                paused++;
            if (e.TryGetComponent<BatteryComponent>(uid, out var bat))
            {
                batteries++;
                charge += bat.CurrentCharge;
                maxCharge += bat.MaxCharge;
            }

            if (e.TryGetComponent<ApcComponent>(uid, out var apc) && !apc.MainBreakerEnabled)
                breakersOff++;
            if (e.TryGetComponent<DoorComponent>(uid, out var door))
            {
                if (door.State == DoorState.Open)
                    doorsOpen++;
                else if (door.State == DoorState.Closed)
                    doorsClosed++;
                else
                    doorsOther++;
            }

            if (e.TryGetComponent<DoorBoltComponent>(uid, out var bolt) && bolt.BoltsDown)
                bolted++;
            if (e.TryGetComponent<WeldableComponent>(uid, out var weld) && weld.IsWelded)
                welded++;
            if (e.TryGetComponent<PoweredLightComponent>(uid, out var light))
            {
                if (light.On)
                    lightsOn++;
                else
                    lightsOff++;
            }

            if (e.TryGetComponent<ThrusterComponent>(uid, out var thruster))
            {
                if (thruster.Enabled)
                    thrustersOn++;
                else
                    thrustersOff++;
                if (thruster.IsOn)
                    thrustersFiring++;
            }

            if (TryCompNamed(ctx, uid, "Wires", out var wires) && GetMember(wires, "WiresList") is ICollection { Count: 0 })
                wiresEmpty++;
            if (TryCompNamed(ctx, uid, "DeviceNetwork", out var dn) && string.IsNullOrEmpty(GetMember(dn, "Address") as string))
                noAddress++;

            if (e.TryGetComponent<SolutionComponent>(uid, out var sol))
            {
                solutions++;
                solVolume += sol.Solution.Volume;
            }

            if (e.TryGetComponent<DamageableComponent>(uid, out var dmg) && dmg.TotalDamage > 0)
            {
                damaged++;
                damage += dmg.TotalDamage;
            }

            if (e.TryGetComponent<StackComponent>(uid, out var stack))
                stackTotal += stack.Count;
            if (TryCompNamed(ctx, uid, "DeviceLinkSource", out var source) && GetMember(source, "LinkedPorts") is IDictionary ports)
            {
                foreach (var v in ports.Values)
                    links += v is ICollection c ? c.Count : 1;
            }

            if (TryCompNamed(ctx, uid, "DeviceList", out var list) && GetMember(list, "Devices") is ICollection devs)
                deviceListDevices += devs.Count;
            if (e.HasComponent<HTNComponent>(uid))
                htn++;
            if (TryCompNamed(ctx, uid, "Docking", out var dock) && GetMember(dock, "DockedWith") is EntityUid)
                docked++;
            if (e.TryGetComponent<WiresPanelComponent>(uid, out var panel) && panel.Open)
                panelsOpen++;

            foreach (var comp in e.GetComponents(uid))
            {
                if (comp is GridAtmosphereComponent)
                    continue;
                foreach (var f in FieldsOf(comp.GetType()))
                {
                    if (f.Info.FieldType == typeof(GasMixture) && f.Info.GetValue(comp) is GasMixture g)
                        compGas += g.TotalMoles;
                }
            }
        }

        p["entities"] = ents.Count.ToString();
        p["entities.anchored"] = anchored.ToString();
        p["entities.contained"] = contained.ToString();
        p["entities.notMapInit"] = notInit.ToString();
        p["entities.paused"] = paused.ToString();
        p["battery.count"] = batteries.ToString();
        p["battery.charge%"] = maxCharge > 0 ? (charge / maxCharge).ToString("P1", Inv) : "-";
        p["apc.breakersOff"] = breakersOff.ToString();
        p["door.open/closed/moving"] = $"{doorsOpen}/{doorsClosed}/{doorsOther}";
        p["door.bolted"] = bolted.ToString();
        p["door.welded"] = welded.ToString();
        p["light.on/off"] = $"{lightsOn}/{lightsOff}";
        p["thruster.enabled/disabled"] = $"{thrustersOn}/{thrustersOff}";
        p["thruster.isOn"] = thrustersFiring.ToString();
        p["wires.emptyLayout"] = wiresEmpty.ToString();
        p["deviceNet.noAddress"] = noAddress.ToString();
        p["solution.count"] = solutions.ToString();
        p["solution.volume"] = solVolume.ToString();
        p["damage.entities"] = damaged.ToString();
        p["damage.total"] = damage.ToString();
        p["stack.total"] = stackTotal.ToString();
        p["deviceLink.links"] = links.ToString();
        p["deviceList.devices"] = deviceListDevices.ToString();
        p["npc.htn"] = htn.ToString();
        p["dock.docked"] = docked.ToString();
        p["wiresPanel.open"] = panelsOpen.ToString();
        p["gas.componentMixtures(mol)"] = compGas.ToString("0.#", Inv);
        p["decals"] = DecalCount(ctx, grid).ToString();
        p["pipe.moles"] = PipeMoles(ctx, grid, out var nets).ToString("0.#", Inv);
        p["pipe.nets"] = nets.ToString();
        if (e.TryGetComponent<GridAtmosphereComponent>(grid, out var atmos))
        {
            var withAir = atmos.Tiles.Values.Where(t => t.Air != null).ToList();
            p["atmos.tilesWithAir"] = withAir.Count.ToString();
            p["atmos.moles"] = withAir.Sum(t => t.Air!.TotalMoles).ToString("0.#", Inv);
            p["atmos.plasma"] = TilePlasma(ctx, grid).ToString("0.##", Inv);
        }

        p["grid.name"] = e.GetComponent<MetaDataComponent>(grid).EntityName;
        p["grid.components"] = e.GetComponents(grid).Count().ToString();
        return p;
    }

    private static int DecalCount(Ctx ctx, EntityUid grid)
    {
        return ctx.E.TryGetComponent<DecalGridComponent>(grid, out var dg)
            ? dg.ChunkCollection.ChunkCollection.Values.Sum(c => c.Decals.Count)
            : 0;
    }

    private static float TilePlasma(Ctx ctx, EntityUid grid)
    {
        return ctx.E.TryGetComponent<GridAtmosphereComponent>(grid, out var atmos)
            ? atmos.Tiles.Values.Where(t => t.Air != null).Sum(t => t.Air!.GetMoles(Gas.Plasma))
            : 0f;
    }

    private static float PipeMoles(Ctx ctx, EntityUid grid, out int nets)
    {
        var seen = new HashSet<IPipeNet>();
        foreach (var uid in AllOnGrid(ctx, grid))
        {
            if (!ctx.E.TryGetComponent<NodeContainerComponent>(uid, out var nodes))
                continue;
            foreach (var node in nodes.Nodes.Values)
            {
                if (node is PipeNode { NodeGroup: IPipeNet net })
                    seen.Add(net);
            }
        }

        nets = seen.Count;
        return seen.Sum(n => n.Air.TotalMoles);
    }

    private static Dictionary<string, int> PruneCounts(Ctx ctx, EntityUid grid)
    {
        var counts = new Dictionary<string, int> { ["Storage.StoredItems"] = 0, ["DeviceLinkSource.LinkedPorts"] = 0, ["ToggleableClothing.ClothingUids"] = 0 };
        foreach (var uid in AllOnGrid(ctx, grid))
        {
            foreach (var key in counts.Keys.ToList())
            {
                var parts = key.Split('.');
                if (TryCompNamed(ctx, uid, parts[0], out var comp) && GetMember(comp, parts[1]) is ICollection c)
                    counts[key] += c.Count;
            }
        }

        return counts;
    }

    #endregion

    #region Snapshot and diff

    private sealed class EntSnap
    {
        public string Key = "";
        public string Proto = "";
        public bool Savable;
        public bool Contained;
        public bool Paused;
        public EntityLifeStage LifeStage;
        public readonly Dictionary<string, Dictionary<string, string>> Comps = new();
        public string Sig = "";
    }

    private sealed class Snap
    {
        public readonly Dictionary<EntityUid, string> Keys = new();
        public readonly List<EntSnap> Ents = new();
    }

    private sealed record FieldMeta(FieldInfo Info, string Name, bool DataField, bool AutoPaused, bool TimeOffset);

    private static readonly Dictionary<Type, List<FieldMeta>> FieldCache = new();

    private static List<FieldMeta> FieldsOf(Type type)
    {
        lock (FieldCache)
        {
            if (FieldCache.TryGetValue(type, out var cached))
                return cached;

            var list = new List<FieldMeta>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(Component) && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(flags))
                {
                    if (typeof(Delegate).IsAssignableFrom(f.FieldType) || f.FieldType == typeof(GameTick))
                        continue;

                    var name = f.Name;
                    MemberInfo attrSource = f;
                    if (name.StartsWith('<'))
                    {
                        name = name[1..name.IndexOf('>')];
                        if (t.GetProperty(name, flags) is { } prop)
                            attrSource = prop;
                    }

                    var raw = attrSource.GetCustomAttributes(true);
                    var attrs = raw.Select(a => a.GetType().Name).ToHashSet();
                    var offset = raw.Any(a => (GetMember(a, "CustomTypeSerializer") as Type)?.Name.Contains("TimeOffset") == true);
                    list.Add(new FieldMeta(f, name,
                        attrs.Contains("DataFieldAttribute") || attrs.Contains("IncludeDataFieldAttribute"),
                        attrs.Contains("AutoPausedFieldAttribute"),
                        offset));
                }
            }

            FieldCache[type] = list;
            return list;
        }
    }

    private static List<EntityUid> AllOnGrid(Ctx ctx, EntityUid grid)
    {
        var list = new List<EntityUid> { grid };
        for (var i = 0; i < list.Count; i++)
        {
            var children = ctx.E.GetComponent<TransformComponent>(list[i]).ChildEnumerator;
            while (children.MoveNext(out var child))
                list.Add(child);
        }

        return list;
    }

    private static Dictionary<string, int> CountByProto(Ctx ctx, EntityUid grid)
    {
        var counts = new Dictionary<string, int>();
        foreach (var uid in AllOnGrid(ctx, grid))
        {
            var meta = ctx.E.GetComponent<MetaDataComponent>(uid);
            var key = (meta.EntityPrototype?.ID ?? "(no prototype)") + (meta.EntityPrototype?.MapSavable == false ? " [MapSavable=false]" : "");
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts;
    }

    private static Snap TakeSnap(Ctx ctx, EntityUid grid)
    {
        var snap = new Snap();
        var order = AllOnGrid(ctx, grid);
        snap.Keys[grid] = "GRID";
        foreach (var uid in order.Skip(1))
            snap.Keys[uid] = KeyOf(ctx, uid, snap.Keys);

        foreach (var uid in order)
        {
            var meta = ctx.E.GetComponent<MetaDataComponent>(uid);
            var es = new EntSnap
            {
                Key = snap.Keys[uid],
                Proto = meta.EntityPrototype?.ID ?? "(no prototype)",
                Savable = meta.EntityPrototype?.MapSavable != false,
                Contained = (meta.Flags & MetaDataFlags.InContainer) != 0,
                Paused = meta.EntityPaused,
                LifeStage = meta.EntityLifeStage,
            };

            foreach (var comp in ctx.E.GetComponents(uid))
            {
                string name;
                try
                {
                    name = ctx.Factory.GetRegistration(comp.GetType()).Name;
                }
                catch
                {
                    name = comp.GetType().Name;
                }

                var fields = new Dictionary<string, string>();
                foreach (var f in FieldsOf(comp.GetType()))
                {
                    try
                    {
                        fields[f.Name] = Fmt(ctx, f.Info.GetValue(comp), snap.Keys);
                    }
                    catch (Exception ex)
                    {
                        fields[f.Name] = "!" + ex.GetType().Name;
                    }
                }

                es.Comps[name] = fields;
            }

            var sig = new StringBuilder();
            foreach (var (comp, fields) in es.Comps.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                sig.Append(comp).Append('{');
                foreach (var (f, v) in fields)
                    sig.Append(f).Append('=').Append(v).Append(',');
                sig.Append('}');
            }

            es.Sig = sig.ToString();
            snap.Ents.Add(es);
        }

        return snap;
    }

    private static string R(float v)
    {
        return (MathF.Round(v, 1) + 0f).ToString("0.0", Inv);
    }

    private static string KeyOf(Ctx ctx, EntityUid uid, Dictionary<EntityUid, string> keys)
    {
        var meta = ctx.E.GetComponent<MetaDataComponent>(uid);
        var xform = ctx.E.GetComponent<TransformComponent>(uid);
        var proto = meta.EntityPrototype?.ID ?? "~" + meta.EntityName;
        if (ctx.Containers.TryGetContainingContainer(new Entity<TransformComponent?, MetaDataComponent?>(uid, xform, meta), out var container))
            return $"{keys.GetValueOrDefault(container.Owner, "?")}/{container.ID}/{proto}";

        var parent = keys.GetValueOrDefault(xform.ParentUid, "?");
        var pos = $"{proto}@{R(xform.LocalPosition.X)},{R(xform.LocalPosition.Y)}";
        return parent == "GRID" ? pos : $"{parent}>{pos}";
    }

    private static string Ref(Ctx ctx, EntityUid uid, Dictionary<EntityUid, string> keys)
    {
        if (keys.TryGetValue(uid, out var key))
            return "@" + key;
        if (!uid.IsValid())
            return "invalid";
        if (!ctx.E.EntityExists(uid))
            return "deleted";
        if (ctx.E.HasComponent<MapComponent>(uid))
            return "OUT:map";
        if (ctx.E.HasComponent<MapGridComponent>(uid))
            return "OUT:grid";
        return "OUT:" + (ctx.E.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "noproto");
    }

    private static bool HoldsEntities(Type t)
    {
        return t == typeof(EntityUid) || t == typeof(NetEntity) || t == typeof(EntityUid?) || t == typeof(NetEntity?);
    }

    private static string Fmt(Ctx ctx, object? v, Dictionary<EntityUid, string> keys)
    {
        switch (v)
        {
            case null:
                return "null";
            case EntityUid u:
                return Ref(ctx, u, keys);
            case NetEntity n:
                return Ref(ctx, ctx.E.GetEntity(n), keys);
            case string s:
                return s.Length > 48 ? "\"" + s[..48] + "...\"" : "\"" + s + "\"";
            case Enum en:
                return en.ToString();
            case bool b:
                return b ? "true" : "false";
            case float f:
                return f.ToString("0.###", Inv);
            case double d:
                return d.ToString("0.###", Inv);
            case TimeSpan t:
                return "t" + t.TotalSeconds.ToString("0.##", Inv);
            case FixedPoint2 fp:
                return fp.ToString();
            case Vector2 v2:
                return $"({R(v2.X)},{R(v2.Y)})";
            case Angle a:
                return a.Degrees.ToString("0.#", Inv) + "deg";
            case EntityCoordinates ec:
                return Ref(ctx, ec.EntityId, keys) + $"({R(ec.Position.X)},{R(ec.Position.Y)})";
            case Solution sol:
                return $"sol {sol.Volume}/{sol.MaxVolume}[{string.Join(",", sol.Contents.Take(4).Select(q => q.Reagent.Prototype + ":" + q.Quantity))}]";
            case GasMixture g:
                return $"gas {g.TotalMoles:0.##}mol@{g.Temperature:0}K";
            case DamageSpecifier ds:
                return "dmg " + ds.GetTotal();
            case IDictionary dict:
            {
                var args = dict.GetType().IsGenericType ? dict.GetType().GetGenericArguments() : Type.EmptyTypes;
                if (!args.Any(HoldsEntities))
                    return $"dict[{dict.Count}]";
                var items = new List<string>();
                foreach (DictionaryEntry de in dict)
                {
                    var k = de.Key is EntityUid or NetEntity ? Fmt(ctx, de.Key, keys) : "";
                    var val = de.Value is EntityUid or NetEntity ? Fmt(ctx, de.Value, keys) : "";
                    items.Add(k + (val.Length > 0 ? "=" + val : ""));
                }

                items.Sort(StringComparer.Ordinal);
                return $"dict[{dict.Count}]{{{string.Join(",", items.Take(8))}}}";
            }
            case IEnumerable seq:
            {
                var type = seq.GetType();
                var elem = type.IsArray ? type.GetElementType() : type.IsGenericType ? type.GetGenericArguments().FirstOrDefault() : null;
                var count = seq is ICollection c ? c.Count : -1;
                if (elem == null || !HoldsEntities(elem))
                    return count >= 0 ? $"seq[{count}]" : "seq";
                var items = new List<string>();
                foreach (var item in seq)
                    items.Add(Fmt(ctx, item, keys));
                items.Sort(StringComparer.Ordinal);
                return $"seq[{items.Count}]{{{string.Join(",", items.Take(8))}}}";
            }
        }

        var vt = v.GetType();
        if (vt.IsPrimitive || vt == typeof(decimal))
            return Convert.ToString(v, Inv) ?? "";
        if (vt.IsValueType)
        {
            var s = v.ToString() ?? "";
            return s.Length > 64 ? s[..64] : s;
        }

        return "obj";
    }

    private sealed class Tally
    {
        public int Count;
        public string Example = "";
    }

    private static void Bump(Dictionary<string, Tally> d, string key, string example, int by = 1)
    {
        if (!d.TryGetValue(key, out var t))
            d[key] = t = new Tally { Example = example };
        t.Count += by;
    }

    private static string RenderTallies(Dictionary<string, Tally> d, int top)
    {
        var sb = new StringBuilder();
        foreach (var (k, t) in d.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key).Take(top))
            sb.AppendLine($"  {t.Count,6}  {k}   e.g. {t.Example}");
        if (d.Count > top)
            sb.AppendLine($"  ... {d.Count - top} more kinds");
        if (d.Count == 0)
            sb.AppendLine("  none");
        return sb.ToString();
    }

    private sealed class Leg
    {
        public readonly string Name;
        public readonly List<string> Notes = new();
        public int Before;
        public int After;
        public int Matched;
        public readonly Dictionary<string, Tally> Missing = new();
        public readonly Dictionary<string, Tally> Added = new();
        public readonly Dictionary<string, Tally> CompLost = new();
        public readonly Dictionary<string, Tally> CompAdded = new();
        public readonly Dictionary<string, Tally> FieldChanged = new();
        public readonly Dictionary<string, Tally> RefKept = new();
        public readonly Dictionary<string, Tally> RefLost = new();
        public readonly Dictionary<string, Tally> OutRefs = new();
        public readonly Dictionary<string, Tally> TimeSpans = new();

        public Leg(string name)
        {
            Name = name;
        }

        public void Absorb(Leg other)
        {
            Before += other.Before;
            After += other.After;
            Matched += other.Matched;
            foreach (var (mine, theirs) in new[]
                     {
                         (Missing, other.Missing), (Added, other.Added), (CompLost, other.CompLost), (CompAdded, other.CompAdded),
                         (FieldChanged, other.FieldChanged), (RefKept, other.RefKept), (RefLost, other.RefLost),
                         (OutRefs, other.OutRefs), (TimeSpans, other.TimeSpans),
                     })
            {
                foreach (var (k, t) in theirs)
                    Bump(mine, k, t.Example, t.Count);
            }
        }

        public string Render(int top)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"---- leg {Name}: {Before} entities before, {After} after, {Matched} matched by key {string.Join("; ", Notes)}");
            sb.AppendLine("2. entities missing after load (by prototype):");
            sb.Append(RenderTallies(Missing, top));
            sb.AppendLine("2. entities new after load:");
            sb.Append(RenderTallies(Added, top));
            sb.AppendLine("3. components lost on matched entities:");
            sb.Append(RenderTallies(CompLost, top));
            sb.AppendLine("3. components that appeared on matched entities:");
            sb.Append(RenderTallies(CompAdded, top));
            sb.AppendLine("4. fields that changed on matched entities ([DF]=DataField, [AP]=AutoPausedField, [TO]=TimeOffsetSerializer, [--]=not saved):");
            sb.Append(RenderTallies(FieldChanged, top));
            sb.AppendLine("4. TimeSpan fields: outcome by kind:");
            sb.Append(RenderTallies(TimeSpans, top));
            sb.AppendLine("5. references to entities on the same grid that survived (field: count):");
            sb.Append(RenderTallies(RefKept, top));
            sb.AppendLine("5. references to entities on the same grid that were LOST:");
            sb.Append(RenderTallies(RefLost, top));
            sb.AppendLine("5. references that pointed OUTSIDE the grid before saving (field -> value after):");
            sb.Append(RenderTallies(OutRefs, top));
            return sb.ToString();
        }
    }

    private static void Diff(Snap before, Snap after, Leg leg)
    {
        leg.Before += before.Ents.Count;
        leg.After += after.Ents.Count;
        var b = before.Ents.GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Sig, StringComparer.Ordinal).ToList());
        var a = after.Ents.GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Sig, StringComparer.Ordinal).ToList());

        foreach (var key in b.Keys.Union(a.Keys))
        {
            var bl = b.GetValueOrDefault(key) ?? new List<EntSnap>();
            var al = a.GetValueOrDefault(key) ?? new List<EntSnap>();
            var n = Math.Min(bl.Count, al.Count);
            for (var i = n; i < bl.Count; i++)
            {
                var e = bl[i];
                var why = (e.Savable ? "" : " [MapSavable=false]") + (e.Contained ? " (contained)" : "");
                Bump(leg.Missing, e.Proto + why, e.Key);
            }

            for (var i = n; i < al.Count; i++)
                Bump(leg.Added, al[i].Proto, al[i].Key);

            for (var i = 0; i < n; i++)
                DiffEntity(bl[i], al[i], leg);
        }
    }

    private static void DiffEntity(EntSnap b, EntSnap a, Leg leg)
    {
        leg.Matched++;
        foreach (var (comp, bf) in b.Comps)
        {
            if (!a.Comps.TryGetValue(comp, out var af))
            {
                Bump(leg.CompLost, comp, b.Key);
                continue;
            }

            var metas = new Dictionary<string, FieldMeta>();
            foreach (var (field, bv) in bf)
            {
                var av = af.GetValueOrDefault(field, "(absent)");
                var tag = FieldTag(comp, field);
                var name = $"{comp}.{field} {tag}";

                if (bv.Contains("OUT:"))
                    Bump(leg.OutRefs, $"{name} -> {Trim(av)}", $"{b.Key}: {Trim(bv)}");
                else if (bv.Contains('@') && bv != "@" + b.Key)
                    Bump(bv == av ? leg.RefKept : leg.RefLost, name, $"{b.Key}: {Trim(bv)} -> {Trim(av)}");

                if (bv.StartsWith('t') && double.TryParse(bv.AsSpan(1), NumberStyles.Float, Inv, out var bt) && bt != 0)
                {
                    var outcome = av == bv ? "same absolute time" : av == "t0" ? "reset to zero" : "different value";
                    Bump(leg.TimeSpans, $"{tag} {outcome}", $"{name} on {b.Proto}: {bv} -> {av}");
                }

                if (bv == av)
                    continue;

                Bump(leg.FieldChanged, name, $"{b.Proto}: {Trim(bv)} -> {Trim(av)}");
            }
        }

        foreach (var comp in a.Comps.Keys)
        {
            if (!b.Comps.ContainsKey(comp))
                Bump(leg.CompAdded, comp, a.Key);
        }
    }

    private static readonly Dictionary<string, string> TagCache = new();

    /// <summary>DataField / AutoPausedField label of a component field, filled when the context is built.</summary>
    private static string FieldTag(string comp, string field)
    {
        return TagCache.GetValueOrDefault($"{comp}.{field}", "[?]");
    }

    private static string Trim(string s)
    {
        return s.Length > 90 ? s[..90] + "..." : s;
    }

    private static Dictionary<(string Key, int Index), HashSet<string>> CompSets(Ctx ctx, EntityUid grid)
    {
        var keys = new Dictionary<EntityUid, string> { [grid] = "GRID" };
        var result = new Dictionary<(string, int), HashSet<string>>();
        var seen = new Dictionary<string, int>();
        foreach (var uid in AllOnGrid(ctx, grid))
        {
            if (uid != grid)
                keys[uid] = KeyOf(ctx, uid, keys);
            var key = keys[uid];
            var idx = seen[key] = seen.GetValueOrDefault(key) + 1;
            result[(key, idx)] = ctx.E.GetComponents(uid).Select(c => ctx.Factory.GetRegistration(c.GetType()).Name).ToHashSet();
        }

        return result;
    }

    private static void DiffCompSets(Dictionary<(string Key, int Index), HashSet<string>> before,
        Dictionary<(string Key, int Index), HashSet<string>> after,
        Dictionary<string, Tally> removed)
    {
        foreach (var (key, comps) in before)
        {
            if (!after.TryGetValue(key, out var now))
            {
                Bump(removed, "(whole entity gone)", key.Key);
                continue;
            }

            foreach (var c in comps.Except(now))
                Bump(removed, c, key.Key);
        }
    }

    #endregion

    #region Serializer helpers

    /// <summary>Serialises each entity alone with every grid entity's id reserved, and records the first component that throws.</summary>
    private static List<(string Comp, string Proto, string Msg)> FindUnsavable(Ctx ctx, EntityUid grid)
    {
        var found = new List<(string, string, string)>();
        var options = SaveOptions;
        var ents = new HashSet<EntityUid>();
        var probe = new EntitySerializer(ctx.Deps, options);
        foreach (var uid in AllOnGrid(ctx, grid))
        {
            if (probe.IsSerializable(uid))
                ents.Add(uid);
        }

        EntitySerializer Fresh()
        {
            var s = new EntitySerializer(ctx.Deps, options);
            s.ReserveYamlIds(ents);
            return s;
        }

        var serializer = Fresh();
        foreach (var uid in ents)
        {
            try
            {
                serializer.SerializeEntity(uid);
            }
            catch (Exception e)
            {
                var proto = ctx.E.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "(no prototype)";
                found.Add((serializer.CurrentComponent ?? "(none)", proto, $"{e.GetType().Name}: {Trim(e.Message)}"));
                serializer = Fresh();
            }
        }

        return found;
    }

    private static void DescribeYaml(MappingDataNode node, string? text, string leg, StringBuilder sb)
    {
        var entities = 0;
        var mapInit = 0;
        var paused = 0;
        var missingComps = 0;
        var invalidByComp = new Dictionary<string, Tally>();
        var bytesByProto = new Dictionary<string, int>();

        if (node.TryGet<SequenceDataNode>("entities", out var protos))
        {
            foreach (var protoGroup in protos.Cast<MappingDataNode>())
            {
                var protoId = protoGroup.TryGet<ValueDataNode>("proto", out var pv) ? pv.Value : "?";
                if (!protoGroup.TryGet<SequenceDataNode>("entities", out var ents))
                    continue;
                foreach (var ent in ents.Cast<MappingDataNode>())
                {
                    entities++;
                    if (ent.ContainsKey("mapInit"))
                        mapInit++;
                    if (ent.ContainsKey("paused"))
                        paused++;
                    if (ent.ContainsKey("missingComponents"))
                        missingComps++;
                    if (!ent.TryGet<SequenceDataNode>("components", out var comps))
                        continue;
                    foreach (var comp in comps.Cast<MappingDataNode>())
                    {
                        var type = comp.TryGet<ValueDataNode>("type", out var tv) ? tv.Value : "?";
                        var invalid = CountInvalid(comp);
                        if (invalid > 0)
                            Bump(invalidByComp, type, protoId, invalid);
                    }
                }
            }
        }

        var bytes = text == null ? 0 : Encoding.UTF8.GetByteCount(text);
        var gz = text == null ? 0 : Gzip(text);
        sb.AppendLine($"  {leg} yaml: {entities} entities, {bytes} bytes, {gz} gzip bytes ({(bytes == 0 ? 0 : 100.0 * gz / bytes):0.#}%), {(entities == 0 ? 0 : bytes / entities)} bytes/entity; " +
                      $"{mapInit} written mapInit:true, {paused} with a paused flag, {missingComps} with missingComponents");
        sb.AppendLine($"  {leg} yaml values written as 'invalid' (references the save could not hold) by component:");
        sb.Append(RenderTallies(invalidByComp, TopRows));
    }

    private static int CountInvalid(DataNode node)
    {
        switch (node)
        {
            case ValueDataNode v:
                return v.Value == "invalid" ? 1 : 0;
            case MappingDataNode m:
            {
                var n = 0;
                foreach (var (k, child) in m)
                    n += (k == "invalid" ? 1 : 0) + CountInvalid(child);
                return n;
            }
            case SequenceDataNode s:
                return s.Sum(CountInvalid);
            default:
                return 0;
        }
    }

    private static int Gzip(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            gz.Write(bytes, 0, bytes.Length);
        }

        return (int) ms.Length;
    }

    private static string Median(IEnumerable<double> values)
    {
        var list = values.OrderBy(v => v).ToList();
        return list.Count == 0 ? "-" : list[list.Count / 2].ToString("0", Inv);
    }

    #endregion

    #region Reflection helpers

    private static bool TryCompNamed(Ctx ctx, EntityUid uid, string name, out IComponent comp)
    {
        comp = default!;
        if (!ctx.Factory.TryGetRegistration(name, out var reg))
            return false;
        if (!ctx.E.TryGetComponent(uid, reg.Type, out var c))
            return false;
        comp = c;
        return true;
    }

    private static object? GetMember(object o, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = o.GetType();
        if (t.GetField(name, flags) is { } f)
            return f.GetValue(o);
        return t.GetProperty(name, flags)?.GetValue(o);
    }

    private static void SetMember(object o, string name, object? value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = o.GetType();
        if (t.GetField(name, flags) is { } f)
            f.SetValue(o, value);
        else
            t.GetProperty(name, flags)?.SetValue(o, value);
    }

    private static ResPath ResolvePath(Ctx ctx, string source)
    {
        if (!source.StartsWith("vessel:"))
            return new ResPath(source);
        return ctx.Protos.Index<VesselPrototype>(source["vessel:".Length..]).ShuttlePath;
    }

    #endregion

    #region Context

    private sealed class Ctx
    {
        public readonly TestPair Pair;
        public readonly IEntityManager E;
        public readonly IComponentFactory Factory;
        public readonly IPrototypeManager Protos;
        public readonly IDependencyCollection Deps;
        public readonly SharedMapSystem Maps;
        public readonly MapLoaderSystem Loader;
        public readonly SharedContainerSystem Containers;
        public readonly ShipyardSystem Yard;
        private readonly IGameTiming _timing;
        private readonly List<(string Phase, string Level, string Sawmill, string Message)> _logs = new();
        public string Phase = "";
        public readonly Leg Settled = new("all grids settled");

        public Ctx(TestPair pair)
        {
            Pair = pair;
            E = pair.Server.EntMan;
            Factory = pair.Server.ResolveDependency<IComponentFactory>();
            Protos = pair.Server.ResolveDependency<IPrototypeManager>();
            Deps = E.EntitySysManager.DependencyCollection;
            _timing = pair.Server.ResolveDependency<IGameTiming>();
            Maps = E.System<SharedMapSystem>();
            Loader = E.System<MapLoaderSystem>();
            Containers = E.System<SharedContainerSystem>();
            Yard = E.System<ShipyardSystem>();

            // Label every field once per component type as it is seen.
            foreach (var reg in Factory.AllRegisteredTypes)
            {
                try
                {
                    var name = Factory.GetRegistration(reg).Name;
                    foreach (var f in FieldsOf(reg))
                        TagCache.TryAdd($"{name}.{f.Name}", (f.DataField ? "[DF]" : "[--]") + (f.AutoPaused ? "[AP]" : "") + (f.TimeOffset ? "[TO]" : ""));
                }
                catch
                {
                    // Not a component we can describe; the diff shows [?].
                }
            }
        }

        public int Ticks(float seconds)
        {
            return Math.Max(1, (int) (seconds * _timing.TickRate));
        }

        /// <summary>Records warnings and errors instead of failing the test on them.</summary>
        public IDisposable CaptureLogs()
        {
            var handler = Pair.ServerLogHandler;
            var previous = handler.FailureLevel;
            handler.FailureLevel = LogLevel.Warning;
            Func<string, Serilog.Events.LogEvent, bool> judge = (sawmill, ev) =>
            {
                lock (_logs)
                    _logs.Add((Phase, ev.Level.ToString(), sawmill, ev.RenderMessage()));
                return true;
            };
            handler.JudgeLog += judge;
            return new Restore(() =>
            {
                handler.JudgeLog -= judge;
                handler.FailureLevel = previous;
            });
        }

        public void ClearLogs()
        {
            lock (_logs)
                _logs.Clear();
        }

        public string RenderLogs(int top)
        {
            var tallies = new Dictionary<string, Tally>();
            lock (_logs)
            {
                foreach (var (phase, level, sawmill, message) in _logs)
                {
                    var shape = new string(message.Select(ch => char.IsDigit(ch) ? '#' : ch).ToArray());
                    if (shape.Length > 110)
                        shape = shape[..110];
                    Bump(tallies, $"{level} {sawmill} [{phase}]: {shape}", Trim(message));
                }
            }

            return RenderTallies(tallies, top);
        }

        private sealed class Restore : IDisposable
        {
            private readonly Action _action;

            public Restore(Action action)
            {
                _action = action;
            }

            public void Dispose()
            {
                _action();
            }
        }
    }

    #endregion
}
