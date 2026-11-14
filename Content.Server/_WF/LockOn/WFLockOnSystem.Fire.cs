using Content.Server._Mono.Projectiles.TargetGuided;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server._WF.ShipMissiles;
using Content.Shared._WF.LockOn;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;

namespace Content.Server._WF.LockOn;

public sealed partial class WFLockOnSystem
{
    [Dependency] private TargetSeekingSystem _targetSeeking = default!;

    /// <summary>How long one fire command keeps applying the ship's lock to that weapon's projectiles.</summary>
    public static readonly TimeSpan ShotLifetime = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Records the ship's lock on each weapon a console is about to fire, so the projectiles it spawns can be
    /// pointed at the locked target before their first guidance tick.
    /// </summary>
    internal void MarkFire(EntityUid console, List<NetEntity>? selected)
    {
        if (selected == null || selected.Count == 0)
            return;

        var ship = Transform(console).GridUid;
        var lockOn = ship is { } grid && TryComp<WFLockOnComponent>(grid, out var found) ? found : null;

        foreach (var net in selected)
        {
            if (!TryGetEntity(net, out var entity) || entity is not { } weapon || TerminatingOrDeleted(weapon))
                continue;

            if (lockOn?.Target is not { } target)
            {
                RemComp<WFLockOnShotComponent>(weapon);
                continue;
            }

            var shot = EnsureComp<WFLockOnShotComponent>(weapon);
            shot.Target = target;
            shot.Expires = _timing.CurTime + ShotLifetime;
        }
    }

    /// <summary>Points the projectiles a locked weapon just fired at the ship's locked target.</summary>
    internal void Apply(EntityUid weapon, AmmoShotEvent args)
    {
        if (!TryComp<WFLockOnShotComponent>(weapon, out var shot) ||
            shot.Target is not { } target ||
            TerminatingOrDeleted(target) ||
            !TryComp<TransformComponent>(target, out var targetXform) ||
            targetXform.MapID == MapId.Nullspace)
            return;

        var targetMap = targetXform.MapPosition;
        var targetCoords = _transform.ToCoordinates(targetMap);
        foreach (var projectile in args.FiredProjectiles)
        {
            if (TerminatingOrDeleted(projectile))
                continue;

            if (TryComp<TargetSeekingComponent>(projectile, out var seeker))
                _targetSeeking.SetSeekerTarget((projectile, seeker), target, Transform(projectile));

            if (TryComp<WFMissileFlightComponent>(projectile, out var flight))
                flight.Designated = targetMap;

            if (TryComp<TargetGuidedComponent>(projectile, out var guided))
                guided.TargetPosition = targetCoords;
        }
    }

    /// <summary>The locked target's live position for a cursor-guided missile fired by this console's ship.</summary>
    internal EntityCoordinates? GuidedTarget(EntityUid console)
    {
        if (Transform(console).GridUid is not { } grid ||
            !TryComp<WFLockOnComponent>(grid, out var lockOn) ||
            lockOn.Target is not { } target ||
            TerminatingOrDeleted(target) ||
            !TryComp<TransformComponent>(target, out var targetXform))
            return null;

        return _transform.ToCoordinates(targetXform.MapPosition);
    }
}
