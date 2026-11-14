using System.Numerics;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.LockOn;
using Content.Shared._WF.ShipMissiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.ShipMissiles;

/// <summary>Computes per-ship ideal missile release data for the fire-control lock overlay.</summary>
public sealed class WFLockOnMissileIndicatorSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float RefreshInterval = 0.1f;
    private float _accumulator;

    public WFLockOnMissileIndicatorSystem()
    {
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator -= frameTime;
        if (_accumulator > 0f) return;
        _accumulator = RefreshInterval;

        var query = EntityQueryEnumerator<WFLockOnComponent, TransformComponent>();
        while (query.MoveNext(out var ship, out var lockOn, out var shipXform))
            WfRefresh(ship, shipXform, lockOn);
    }

    private void WfRefresh(EntityUid ship, TransformComponent shipXform, WFLockOnComponent lockOn)
    {
        if (lockOn.Target is not { } targetUid || !Exists(targetUid))
        {
            if (lockOn.MissileHasSolution) { lockOn.MissileHasSolution = false; Dirty(ship, lockOn); }
            return;
        }

        var targetPos = lockOn.AimTime > 0f
            ? lockOn.AimPoint + lockOn.AimVelocity * ((float)_timing.CurTime.TotalSeconds - lockOn.AimTime)
            : _transform.GetWorldPosition(Transform(targetUid));
        var targetVel = lockOn.AimTime > 0f ? lockOn.AimVelocity : Vector2.Zero;
        var shipVel = _physics.GetMapLinearVelocity(ship);

        MissileCandidate? best = null;

        var gunQuery = EntityQueryEnumerator<GunComponent, TransformComponent, ShipGunTypeComponent>();
        while (gunQuery.MoveNext(out var gunUid, out var gun, out var gunXform, out var gunType))
        {
            if (gunType.Type != ShipGunType.Missile || gunXform.GridUid != ship) continue;
            if (!_gun.TryNextShootPrototype((gunUid, gun), out var proto)) continue;
            proto = _gun.GetBulletPrototype(proto);
            if (!proto.TryComp<WFMissileFlightComponent>(out var m, Factory)) continue;

            var muzzle = _transform.GetWorldPosition(gunXform);
            var nose = _transform.GetWorldRotation(gunXform).ToWorldVec();
            var cand = Solve(muzzle, shipVel, nose, targetPos, targetVel, m);
            if (!cand.HasSolution) continue;
            if (best == null || cand.Quality > best.Value.Quality) best = cand;
        }

        if (best == null)
        {
            if (lockOn.MissileHasSolution) { lockOn.MissileHasSolution = false; Dirty(ship, lockOn); }
            return;
        }

        var b = best.Value;
        lockOn.MissileBearing = b.Bearing;
        lockOn.MissileMuzzle = b.Muzzle;
        lockOn.MissileDivertCost = b.DivertCost;
        lockOn.MissileDivertBudget = b.DivertBudget;
        lockOn.MissileQuality = b.Quality;
        lockOn.MissileInEnvelope = b.InEnvelope;
        lockOn.MissileRange = b.Range;
        lockOn.MissileHasSolution = true;
        Dirty(ship, lockOn);
    }

    private readonly record struct MissileCandidate(
        Vector2 Bearing, Vector2 Muzzle, float DivertCost, float DivertBudget,
        float Quality, bool InEnvelope, bool HasSolution, float Range);

    private static MissileCandidate Solve(Vector2 muzzle, Vector2 shipVel, Vector2 nose,
        Vector2 targetPos, Vector2 targetVel, WFMissileFlightComponent m)
    {
        var intercept = WFLockOnMath.Solve(muzzle, targetPos, targetVel, m.MaxSpeed);
        if (!intercept.Valid)
            return new(Vector2.Zero, muzzle, 0f, m.DivertBudget, 0f, false, false, 0f);

        var idealBearing = (intercept.Point - muzzle).Normalized();
        var goal = MissileGuidance.ClampBearing(nose, idealBearing, m.LaunchOffBoresight);
        var bearing = MissileGuidance.BlendDirection(nose, goal, m.SnapFraction);
        var inEnvelope = Vector2.Dot(goal, idealBearing) > 0.999f;

        var v0 = m.SnapFraction >= 1f
            ? bearing * (shipVel + bearing * m.LaunchSpeed).Length()
            : shipVel + bearing * m.LaunchSpeed;
        var v0Mag = v0.Length();
        var errAngle = v0Mag > 0.001f
            ? MathF.Acos(Math.Clamp(Vector2.Dot(v0 / v0Mag, idealBearing), -1f, 1f))
            : 0f;
        var rawCost = 2f * v0Mag * MathF.Sin(errAngle * 0.5f);
        var freeTurn = m.LateralAccel * m.LaunchTurnMultiplier * m.LaunchTransient;
        var divertCost = MathF.Max(0f, rawCost - freeTurn);
        var quality = m.DivertBudget > 0f
            ? 1f - Math.Clamp(divertCost / m.DivertBudget, 0f, 1f)
            : (divertCost <= 0.001f ? 1f : 0f);

        return new(idealBearing, muzzle, divertCost, m.DivertBudget, quality, inEnvelope, true,
            (intercept.Point - muzzle).Length());
    }
}
