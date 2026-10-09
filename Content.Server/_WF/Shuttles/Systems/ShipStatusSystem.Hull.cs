using System.Linq;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Shuttles.Systems;

public sealed partial class ShipStatusSystem
{
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, float>> _hullBaselines = new();
    private readonly HashSet<EntityUid> _sharedHullBaselines = new();
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, (float Capacity, float Remaining)>> _hullStructures = new();
    private readonly Dictionary<EntityUid, float> _hullIntegrity = new();

    /// <summary>Snapshots the hull before piloting can damage it, even if its telemetry page has never opened.</summary>
    public void ObserveHull(EntityUid console)
    {
        if (GetConsoleGrid(console) is { } grid && !_hullBaselines.ContainsKey(grid))
            GatherTiles(new HashSet<EntityUid> { grid });
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
    }

    private void ForgetDeletedHulls()
    {
        foreach (var grid in _hullBaselines.Keys.ToArray())
        {
            if (!TerminatingOrDeleted(grid))
                continue;
            _hullBaselines.Remove(grid);
            _sharedHullBaselines.Remove(grid);
        }
    }
}
