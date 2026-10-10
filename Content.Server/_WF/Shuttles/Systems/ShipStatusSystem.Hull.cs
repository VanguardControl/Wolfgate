using System.Linq;
using Content.Server.Construction;
using Content.Server.Construction.Completions;
using Content.Server.Destructible;
using Content.Shared._WF.Shuttles;
using Content.Shared.Damage;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Shuttles.Systems;

public sealed partial class ShipStatusSystem
{
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, float>> _hullBaselines = new();
    private readonly HashSet<EntityUid> _sharedHullBaselines = new();
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, (float Capacity, float Remaining)>> _hullStructures = new();
    private readonly Dictionary<EntityUid, float> _hullIntegrity = new();
    private readonly Dictionary<EntityUid, HullSurvey> _surveyedHull = new();
    private readonly Dictionary<EntityUid, ConstructionBeforeDeleteEvent?> _hullConstructionRemovals = new();

    private readonly record struct HullSurvey(EntityUid Grid, Vector2i Index, float Capacity);

    private void InitializeHullConstruction()
    {
        SubscribeLocalEvent<DestructibleComponent, ConstructionBeforeDeleteEvent>(OnHullConstructionDelete);
        SubscribeLocalEvent<DestructibleComponent, ConstructionChangeEntityEvent>(OnHullConstructionChange);
        SubscribeLocalEvent<DestructibleComponent, WFHullDeconstructedEvent>(OnHullRcdDelete);
        SubscribeLocalEvent<DestructibleComponent, EntityTerminatingEvent>(OnHullTerminating);
    }

    private void ObserveHullStructure(EntityUid uid, EntityUid grid, Vector2i index, float capacity) =>
        _surveyedHull[uid] = new HullSurvey(grid, index, capacity);

    private void OnHullConstructionDelete(EntityUid uid, DestructibleComponent component, ConstructionBeforeDeleteEvent args)
    {
        if (_surveyedHull.ContainsKey(uid) && !HullDestroyedByDamage(uid, component))
            _hullConstructionRemovals[uid] = args;
    }

    private void OnHullConstructionChange(EntityUid uid, DestructibleComponent component, ConstructionChangeEntityEvent args)
    {
        if (args.Old == uid && _surveyedHull.ContainsKey(uid) && !HullDestroyedByDamage(uid, component))
            _hullConstructionRemovals[uid] = null;
    }

    /// <summary>Destruction behaviors can also replace construction nodes and must retain their damage.</summary>
    private bool HullDestroyedByDamage(EntityUid uid, DestructibleComponent component) =>
        TryComp<DamageableComponent>(uid, out var damage) && damage.TotalDamage >= _destructible.DestroyedAt(uid, component);

    private void OnHullRcdDelete(EntityUid uid, DestructibleComponent component, ref WFHullDeconstructedEvent args)
    {
        if (_surveyedHull.ContainsKey(uid))
            _hullConstructionRemovals[uid] = null;
    }

    /// <summary>Only completed deconstruction changes the design; combat and cancelled actions retain their losses.</summary>
    private void OnHullTerminating(EntityUid uid, DestructibleComponent component, ref EntityTerminatingEvent args)
    {
        var surveyed = _surveyedHull.Remove(uid, out var structure);
        var constructed = _hullConstructionRemovals.Remove(uid, out var beforeDelete) && beforeDelete?.Cancelled != true;
        if (!surveyed || !constructed || !_hullBaselines.TryGetValue(structure.Grid, out var baseline))
            return;
        if (_sharedHullBaselines.Remove(structure.Grid))
            _hullBaselines[structure.Grid] = baseline = new Dictionary<Vector2i, float>(baseline);
        baseline[structure.Index] = Math.Max(0, baseline.GetValueOrDefault(structure.Index) - structure.Capacity);
    }

    /// <summary>Snapshots the hull before piloting can damage it, even if its telemetry page has never opened.</summary>
    public void ObserveHull(EntityUid console)
    {
        if (GetConsoleGrid(console) is { } grid && !_hullBaselines.ContainsKey(grid))
            GatherTiles(new HashSet<EntityUid> { grid }, new HashSet<EntityUid>());
    }

    /// <summary>Retains lost locations and measures each surveyed tile against its own structural capacity.</summary>
    private void GatherHullIntegrity(HashSet<EntityUid> grids)
    {
        foreach (var grid in grids)
        {
            if (!TryComp<MapGridComponent>(grid, out var gridComp))
                continue;
            if (!_hullBaselines.TryGetValue(grid, out var baseline))
                _hullBaselines[grid] = baseline = new Dictionary<Vector2i, float>();
            else if (_sharedHullBaselines.Remove(grid))
                _hullBaselines[grid] = baseline = new Dictionary<Vector2i, float>(baseline);
            foreach (var tile in _maps.GetAllTiles(grid, gridComp))
                baseline.TryAdd(tile.GridIndices, 0f);
            var structures = _hullStructures[grid];
            foreach (var (index, structure) in structures)
                baseline[index] = Math.Max(baseline.GetValueOrDefault(index), structure.Capacity);

            var intact = 0f;
            foreach (var (index, capacity) in baseline)
            {
                if (_maps.GetTileRef(grid, gridComp, index).Tile.IsEmpty)
                    continue;
                intact += capacity > 0f
                    ? Math.Clamp(structures.GetValueOrDefault(index).Remaining / capacity, 0f, 1f)
                    : 1f;
            }
            _hullIntegrity[grid] = baseline.Count > 0 ? intact / baseline.Count : 1f;
        }
    }

    /// <summary>Split grids retain their original coordinate frame and inherit the whole surveyed hull.</summary>
    private void OnHullSplit(ref GridSplitEvent args)
    {
        if (!_hullBaselines.TryGetValue(args.Grid, out var baseline))
            return;
        // Debris shares the snapshot until surveyed; the original must also copy before changing it.
        _sharedHullBaselines.Add(args.Grid);
        foreach (var grid in args.NewGrids)
        {
            _hullBaselines[grid] = baseline;
            _sharedHullBaselines.Add(grid);
        }
        foreach (var (uid, structure) in _surveyedHull.ToArray())
        {
            if (structure.Grid == args.Grid && TryComp<TransformComponent>(uid, out var transform) &&
                transform.GridUid is { } grid && grid != args.Grid)
                _surveyedHull[uid] = structure with { Grid = grid };
        }
    }

    private void ForgetDeletedHulls()
    {
        foreach (var grid in _hullBaselines.Keys.ToArray())
        {
            if (!TerminatingOrDeleted(grid))
                continue;
            _hullBaselines.Remove(grid);
            _sharedHullBaselines.Remove(grid);
            foreach (var (uid, structure) in _surveyedHull.ToArray())
            {
                if (structure.Grid != grid)
                    continue;
                _surveyedHull.Remove(uid);
                _hullConstructionRemovals.Remove(uid);
            }
        }
    }
}
