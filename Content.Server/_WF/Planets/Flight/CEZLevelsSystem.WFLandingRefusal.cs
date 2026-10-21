using System.Numerics;
using Content.Shared.Shuttles.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>How long a clear or blocked landing spot is trusted before it is looked at again.</summary>
    private static readonly TimeSpan WFLandingCheckInterval = TimeSpan.FromSeconds(0.5);

    private static readonly TimeSpan WFLandingRefusalCooldown = TimeSpan.FromSeconds(4);

    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Blocked)> _wfLandingChecks = new();
    private readonly Dictionary<EntityUid, TimeSpan> _wfNextLandingRefusal = new();

    /// <summary>
    /// True when a hull setting down gently would come to rest on something solid, such as rock or a building's walls:
    /// it holds just above instead, and its pilots are told to find clear ground. A crash still comes down regardless.
    /// </summary>
    private bool WfHoldsOffObstacles(EntityUid grid, HashSet<EntityUid> transitSet, EntityUid ground)
    {
        if (_wfLandingChecks.TryGetValue(grid, out var cached) && _timing.CurTime < cached.Until)
            return cached.Blocked;

        var blocked = false;
        foreach (var member in transitSet)
        {
            if (WfObstacleUnder(member, ground))
            {
                blocked = true;
                break;
            }
        }
        _wfLandingChecks[grid] = (_timing.CurTime + WFLandingCheckInterval, blocked);

        if (blocked && _timing.CurTime >= _wfNextLandingRefusal.GetValueOrDefault(grid))
        {
            _wfNextLandingRefusal[grid] = _timing.CurTime + WFLandingRefusalCooldown;
            var pilots = EntityQueryEnumerator<PilotComponent>();
            while (pilots.MoveNext(out var pilotUid, out var pilot))
            {
                if (pilot.Console is { } console && !TerminatingOrDeleted(console) && transitSet.Contains(Transform(console).GridUid ?? EntityUid.Invalid))
                    _popup.PopupEntity(Loc.GetString("wf-flight-landing-blocked"), console, pilotUid);
            }
        }
        return blocked;
    }

    /// <summary>Whether anything hard and anchored on the ground below stands under one of the hull's real tiles.</summary>
    public bool WfObstacleUnder(EntityUid hull, EntityUid ground)
    {
        if (!TryComp<MapGridComponent>(hull, out var hullGrid) || !TryComp<MapGridComponent>(ground, out var groundGrid))
            return false;

        var bounds = _transform.GetWorldMatrix(hull).TransformBox(hullGrid.LocalAABB);
        foreach (var obstacle in _map.GetAnchoredEntities(ground, groundGrid, new Box2Rotated(bounds, Angle.Zero)))
        {
            if (TerminatingOrDeleted(obstacle) || !TryComp<FixturesComponent>(obstacle, out var fixtures))
                continue;
            var solid = false;
            foreach (var fixture in fixtures.Fixtures.Values)
                solid |= fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.Impassable) != 0;
            if (!solid)
                continue;

            var pos = _transform.GetWorldPosition(obstacle);
            var half = new Vector2(groundGrid.TileSize * 0.45f);
            foreach (var tile in _map.GetTilesIntersecting(hull, hullGrid, new Box2(pos - half, pos + half)))
            {
                if (!tile.Tile.IsEmpty)
                    return true;
            }
        }
        return false;
    }
}
