using System.Numerics;
using Content.Shared.Mind.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Cleanup;

/// <summary>Player protection from a position snapshot taken at most once a second, instead of a spatial lookup per candidate.</summary>
public sealed partial class CleanupHelperSystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Longest a player snapshot is used before it is rebuilt.</summary>
    private static readonly TimeSpan WGPlayerSnapshotLifetime = TimeSpan.FromSeconds(1);

    private List<MapCoordinates> _wgPlayers = new();
    private TimeSpan _wgPlayersTakenAt;
    private bool _wgPlayersValid;

    /// <summary>Distance to the nearest player-bound entity on the same map, or infinity if there is none.</summary>
    public float WGNearestPlayerDistance(EntityCoordinates coord)
    {
        var mapPos = _transform.ToMapCoordinates(coord, logError: false);
        if (mapPos.MapId == MapId.Nullspace)
            return float.PositiveInfinity;

        WGRefreshPlayers();

        var bestSquared = float.PositiveInfinity;
        foreach (var player in _wgPlayers)
        {
            if (player.MapId != mapPos.MapId)
                continue;

            var squared = Vector2.DistanceSquared(player.Position, mapPos.Position);
            if (squared < bestSquared)
                bestSquared = squared;
        }

        return MathF.Sqrt(bestSquared);
    }

    /// <summary>Rebuilds the snapshot once it is a second old, with the same player test the lookup used.</summary>
    private void WGRefreshPlayers()
    {
        var now = _timing.CurTime;
        if (_wgPlayersValid && now >= _wgPlayersTakenAt && now - _wgPlayersTakenAt < WGPlayerSnapshotLifetime)
            return;

        _wgPlayersValid = true;
        _wgPlayersTakenAt = now;
        _wgPlayers.Clear();

        // all, not just unpaused: the lookup it replaces found paused players too
        var query = AllEntityQuery<MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var container, out var xform))
        {
            if (!container.HasMind
                || _ghostQuery.HasComp(uid)
                || _mindQuery.CompOrNull(container.Mind.Value)?.OwnedEntity == null)
                continue;

            var pos = _transform.GetMapCoordinates(uid, xform);
            if (pos.MapId != MapId.Nullspace)
                _wgPlayers.Add(pos);
        }
    }
}
