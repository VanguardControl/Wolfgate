using Content.Server.Power.Components;
using Robust.Shared.Map.Components;

namespace Content.Server.Power.EntitySystems;

/// <summary>
/// Engine v292 moves anchored entities by unanchoring and anchoring them again (a grid split, a mapping tool move),
/// so an unanchored cable whose tile is still there waits a tick and is only cut if it stayed unanchored.
/// </summary>
public sealed partial class CableSystem
{
    private readonly HashSet<EntityUid> _wgPendingCut = new();

    /// <summary>
    /// True if the cable was unanchored over a tile that still exists, so the cut waits for the end of the tick.
    /// </summary>
    private bool WGDeferCut(EntityUid uid, TransformComponent xform)
    {
        if (!TryComp(xform.GridUid, out MapGridComponent? grid))
            return false;

        var indices = _map.TileIndicesFor(xform.GridUid.Value, grid, xform.Coordinates);
        if (!_map.TryGetTileRef(xform.GridUid.Value, grid, indices, out var tile) || tile.Tile.IsEmpty)
            return false;

        _wgPendingCut.Add(uid);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_wgPendingCut.Count == 0)
            return;

        foreach (var uid in _wgPendingCut)
        {
            if (TerminatingOrDeleted(uid) || !TryComp(uid, out CableComponent? cable))
                continue;

            var xform = Transform(uid);
            if (xform.Anchored)
                continue;

            Spawn(cable.CableDroppedOnCutPrototype, xform.Coordinates);
            QueueDel(uid);
        }

        _wgPendingCut.Clear();
    }
}
