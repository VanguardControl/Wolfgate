using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Caverns;

/// <summary>
/// Opens the ground's eye cap for viewers who can see one of its holes: their z-level eyes reach the cavern under it, so
/// the cavern streams in and shows through the hole. Everyone else stays capped at the ground.
/// </summary>
public sealed partial class WFCavernEyeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Tiles past the viewer's view of the ground at which a hole brings the cavern in.</summary>
    public const float EnterMargin = 4f;

    /// <summary>Tiles past the viewer's view of the ground every hole must be before the cavern goes again.</summary>
    public const float LeaveMargin = 12f;

    /// <summary>How often viewers are measured against the holes.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    private float _viewRange;
    private TimeSpan _nextCheck;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CVars.NetMaxUpdateRange, range => _viewRange = range, true);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        var query = EntityQueryEnumerator<CEZLevelViewerComponent, ActorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            var seeing = CompOrNull<WFCavernViewerComponent>(uid)?.Ground;

            if (_zLevels.WfGroundInView(uid) is not { } view)
            {
                if (seeing != null)
                    RemComp<WFCavernViewerComponent>(uid);

                continue;
            }

            // A change rebuilds the viewer's eyes, which asks SeesCavern again and records the answer.
            if (Sees(uid, view.Ground, _transform.GetWorldPosition(xform), view.Scale) != (seeing == view.Ground))
                _zLevels.WfQueueViewerUpdate(uid);
        }
    }

    /// <summary>
    /// Whether a viewer whose eyes reach this ground, seeing it at this view scale, gets an eye on the cavern under it.
    /// Records the answer: a viewer who has the cavern keeps it out to <see cref="LeaveMargin"/>.
    /// </summary>
    public bool SeesCavern(EntityUid viewer, EntityUid ground, Vector2 position, float viewScale)
    {
        if (!Sees(viewer, ground, position, viewScale))
        {
            RemComp<WFCavernViewerComponent>(viewer);
            return false;
        }

        EnsureComp<WFCavernViewerComponent>(viewer).Ground = ground;
        return true;
    }

    private bool Sees(EntityUid viewer, EntityUid ground, Vector2 position, float viewScale)
    {
        if (!TryComp<WFCavernGroundComponent>(ground, out var comp) || TerminatingOrDeleted(comp.Cavern))
            return false;

        var seeing = CompOrNull<WFCavernViewerComponent>(viewer)?.Ground == ground;
        var reach = _viewRange * viewScale + (seeing ? LeaveMargin : EnterMargin);
        return AnyHoleWithin(comp, position, reach);
    }

    /// <summary>Whether an open hole tile lies within a square of this half-size around a world position.</summary>
    // Each shade marks one open hole tile, and the ground grid is its own map, so its tiles are world tiles.
    public static bool AnyHoleWithin(WFCavernGroundComponent ground, Vector2 position, float reach)
    {
        foreach (var tile in ground.Shades.Keys)
        {
            var dx = MathF.Max(MathF.Max(tile.X - position.X, position.X - tile.X - 1f), 0f);
            var dy = MathF.Max(MathF.Max(tile.Y - position.Y, position.Y - tile.Y - 1f), 0f);

            if (dx <= reach && dy <= reach)
                return true;
        }

        return false;
    }
}
