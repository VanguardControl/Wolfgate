using System.Numerics;
using Content.Client.Shuttles.UI;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.LockOn;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._WF.LockOn;

/// <summary>
/// Draws the ship's lock bracket, the target's name and a lead cross for every selected gun over a radar
/// plot. It reads the networked <see cref="WFLockOnComponent"/> from the console's ship, so every console
/// aboard shows the same firing solution.
/// </summary>
public sealed class WFLockOnOverlay : Control
{
    private static readonly Color LockColor = Color.FromHex("#ff4a4a");
    private static readonly Color LeadColor = Color.FromHex("#ffd23f");

    private const float BracketRadius = 16f;
    private const float BracketArm = 7f;
    private const float CrossArm = 5f;
    private const float DedupeRadius = 3f;
    private const int MaxGuns = 64;
    private const float MaxExtrapolation = 0.5f;

    private readonly EntityUid _console;
    private readonly ShuttleNavControl _radar;
    private readonly Func<IReadOnlyList<NetEntity>> _weapons;
    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly SharedPhysicsSystem _physics;
    private readonly IGameTiming _timing;
    private readonly IConfigurationManager _config;
    private readonly Font _font;

    private readonly List<(EntityUid Weapon, float Speed)> _guns = new();
    private readonly List<NetEntity> _selection = new();
    private readonly List<Vector2> _leads = new();

    public WFLockOnOverlay(EntityUid console, ShuttleNavControl radar, Func<IReadOnlyList<NetEntity>> weapons)
    {
        _console = console;
        _radar = radar;
        _weapons = weapons;
        _entities = IoCManager.Resolve<IEntityManager>();
        _transform = _entities.System<SharedTransformSystem>();
        _physics = _entities.System<SharedPhysicsSystem>();
        _timing = IoCManager.Resolve<IGameTiming>();
        _config = IoCManager.Resolve<IConfigurationManager>();
        _font = new VectorFont(
            IoCManager.Resolve<IResourceCache>().GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 12);
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (!_entities.TryGetComponent(_console, out TransformComponent? consoleXform) ||
            consoleXform.GridUid is not { } ship ||
            !_entities.TryGetComponent(ship, out WFLockOnComponent? lockOn) ||
            lockOn.Target is not { } target ||
            !_entities.EntityExists(target) ||
            !_entities.TryGetComponent(target, out TransformComponent? targetXform) ||
            targetXform.MapID == MapId.Nullspace ||
            !_radar.WfLockOnTryGetWorldToView(out var worldToView, out var map) ||
            map != targetXform.MapID)
            return;

        // The server measures the clicked hull point; extrapolating it from the last measurement keeps the cross
        // smooth between refreshes without trusting the client's physics for someone else's grid.
        var targetWorld = lockOn.AimTime > 0f
            ? lockOn.AimPoint + lockOn.AimVelocity * Extrapolation(lockOn)
            : _transform.GetWorldPosition(targetXform);
        var targetView = Vector2.Transform(targetWorld, worldToView);
        DrawBracket(handle, targetView);

        if (_entities.TryGetComponent(target, out MetaDataComponent? meta) && !string.IsNullOrEmpty(meta.EntityName))
            handle.DrawString(_font, new Vector2(8, 8), Loc.GetString("wf-lockon-status", ("target", meta.EntityName)), LockColor);

        RefreshGuns();
        if (_guns.Count == 0 && !lockOn.MissileHasSolution)
            return;

        _leads.Clear();
        var debug = _config.GetCVar(WFLockOnCVars.DebugLead);

        foreach (var (weapon, speed) in _guns)
        {
            if (!_entities.TryGetComponent(weapon, out TransformComponent? weaponXform) ||
                weaponXform.MapID != targetXform.MapID)
                continue;

            // A shot leaves with the turn of the grid it is mounted on, as GunSystem computes it.
            var mount = weaponXform.GridUid ?? ship;
            var muzzle = _transform.GetWorldPosition(weaponXform);
            var muzzleVelocity = _physics.GetMapLinearVelocity(weapon);

            var lead = WFLockOnMath.Solve(muzzle, targetWorld, lockOn.AimVelocity - muzzleVelocity, speed);
            if (!lead.Valid)
                continue;

            var leadView = Vector2.Transform(lead.Point, worldToView);
            if (NearbyLead(leadView))
                continue;

            _leads.Add(leadView);
            handle.DrawLine(targetView, leadView, LeadColor.WithAlpha(0.45f));
            handle.DrawLine(leadView - new Vector2(CrossArm, 0), leadView + new Vector2(CrossArm, 0), LeadColor);
            handle.DrawLine(leadView - new Vector2(0, CrossArm), leadView + new Vector2(0, CrossArm), LeadColor);

            if (debug)
            {
                handle.DrawString(_font, leadView + new Vector2(CrossArm, -14f), Loc.GetString("wf-lockon-debug",
                    ("time", $"{lead.Time:0.0}"),
                    ("target", $"{lockOn.AimVelocity.Length():0}"),
                    ("muzzle", $"{muzzleVelocity.Length():0}"),
                    ("speed", $"{speed:0}"),
                    ("lead", $"{(lead.Point - targetWorld).Length():0}")), LeadColor);
            }
        }
        if (lockOn.MissileHasSolution)
        {
            // Ideal launch vector: account for own-ship and target motion so the
            // sector points at the intercept, not at the target's current position.
            //
            // The missile leaves the rail carrying the ship's velocity and adds its
            // own speed along the launch direction; the target keeps moving. Solve
            // for the intercept using the effective closing speed (so a fleeing
            // target lengthens the flight time, a charging one shortens it), drift
            // both ends forward by that flight time, then aim at where they meet.
            // One iteration is enough for sub-second intercepts.
            var shipVel = _physics.GetMapLinearVelocity(ship); // console grid == launching grid in normal play
            var missileSpeed = lockOn.MissileSpeed > 0f ? lockOn.MissileSpeed : 400f;

            var toTarget = targetWorld - lockOn.MissileMuzzle;
            var range = toTarget.Length();
            var dirToTarget = range > 1e-3f ? toTarget / range : Vector2.UnitX;

            var relVel = lockOn.AimVelocity - shipVel;
            var closingSpeed = MathF.Max(missileSpeed - Vector2.Dot(relVel, dirToTarget), 1f);
            var timeOfFlight = range / closingSpeed;

            var leadWorld = targetWorld + relVel * timeOfFlight;
            var leadBearing = leadWorld - lockOn.MissileMuzzle;
            if (leadBearing.LengthSquared() < 1e-6f)
                leadBearing = dirToTarget;
            leadBearing = leadBearing.Normalized();

            var missileLeadView = Vector2.Transform(leadWorld, worldToView);

            var color = lockOn.MissileQuality >= 0.75f ? Color.FromHex("#40e040")
              : lockOn.MissileQuality >= 0.4f ? Color.FromHex("#e0e040")
                                               : Color.FromHex("#e04040");

            DrawQualityRing(handle, missileLeadView, lockOn.MissileQuality, color);

            if (!lockOn.MissileInEnvelope)
                handle.DrawString(_font, missileLeadView + new Vector2(8, 8),
                Loc.GetString("wf-lockon-missile-out-of-envelope"), color);

            if (_config.GetCVar(WFLockOnCVars.DebugLead))
                handle.DrawString(_font, missileLeadView + new Vector2(8, -14), Loc.GetString("wf-lockon-missile-debug", ("cost", $"{lockOn.MissileDivertCost:0}"), ("budget", $"{lockOn.MissileDivertBudget:0}"), ("qual", $"{lockOn.MissileQuality * 100f:0}%"), ("range", $"{lockOn.MissileRange:0}")), color);

            // Sector centered on the LEAD bearing, built in world space so that
            // worldToView applies the Y-flip, ship rotation and radar zoom together.
            var leeway = ComputeLeeway(lockOn.MissileRange, lockOn.MissileQuality);

            var prev = Vector2.Transform(lockOn.MissileMuzzle + Rotate(leadBearing, -leeway) * SectorRadiusPx, worldToView);
            for (var i = 1; i <= SectorArcSteps; i++)
            {
                var t = -leeway + (2f * leeway) * i / SectorArcSteps;
                var next = Vector2.Transform(
                    lockOn.MissileMuzzle + Rotate(leadBearing, t) * SectorRadiusPx,
                    worldToView);
                handle.DrawLine(prev, next, color.WithAlpha(0.7f));
                prev = next;
            }
        }
    }

    /// <summary>Seconds since the server measured the aim point, capped so a stale lock never runs away.</summary>
    private float Extrapolation(WFLockOnComponent lockOn)
    {
        var elapsed = (float)_timing.CurTime.TotalSeconds - lockOn.AimTime;
        return Math.Clamp(elapsed, 0f, MaxExtrapolation);
    }

    private const float SectorRadiusPx = 25f;
    private const float SectorMinLeewayDeg = 15f;
    private const float SectorMaxLeewayDeg = 60f;
    private const float SectorMinRangeM = 200f;
    private const float SectorMaxRangeM = 2000f;
    private const int SectorArcSteps = 16;

    /// <summary>Radians of acceptable launch leeway either side of the ideal bearing.
    /// Widens with range (more flight time to correct) and quality (more divert budget).</summary>
    private float ComputeLeeway(float rangeM, float quality)
    {
        var t = Math.Clamp((rangeM - SectorMinRangeM) / (SectorMaxRangeM - SectorMinRangeM), 0f, 1f);
        var leewayDeg = SectorMinLeewayDeg + t * (SectorMaxLeewayDeg - SectorMinLeewayDeg);
        return leewayDeg * MathF.PI / 180f * quality;
    }

    /// <summary>Rotates a vector by radians.</summary>
    private static Vector2 Rotate(Vector2 v, float radians)
    {
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    /// <summary>Rebuilds the gun list when the selection changes; gun speeds come from the networked component.</summary>
    private void RefreshGuns()
    {
        var weapons = Weapons();
        if (SameSelection(weapons))
            return;

        _selection.Clear();
        _guns.Clear();
        for (var i = 0; i < weapons.Count && i < MaxGuns; i++)
        {
            var net = weapons[i];
            _selection.Add(net);
            if (!_entities.TryGetEntity(net, out var entity) ||
                !_entities.TryGetComponent(entity.Value, out GunComponent? gun) ||
                !_entities.TryGetComponent(entity.Value, out ShipGunTypeComponent? type) ||
                type.Type == ShipGunType.Missile)
                continue;

            var speed = _entities.HasComponent<HitscanBatteryAmmoProviderComponent>(entity.Value)
                ? 0f
                : gun.ProjectileSpeedModified > 0f ? gun.ProjectileSpeedModified : gun.ProjectileSpeed;
            _guns.Add((entity.Value, speed));
        }
    }

    /// <summary>The console's own selection, falling back to the cockpit's linked guns when the helm has none.</summary>
    private IReadOnlyList<NetEntity> Weapons()
    {
        var selected = _weapons();
        if (selected.Count > 0)
            return selected;
        return WFLockOnClientState.CockpitWeapons?.Invoke() ?? Array.Empty<NetEntity>();
    }

    private bool SameSelection(IReadOnlyList<NetEntity> weapons)
    {
        if (weapons.Count != _selection.Count)
            return false;
        for (var i = 0; i < weapons.Count; i++)
        {
            if (weapons[i] != _selection[i])
                return false;
        }
        return true;
    }

    private bool NearbyLead(Vector2 point)
    {
        foreach (var existing in _leads)
        {
            if ((existing - point).LengthSquared() < DedupeRadius * DedupeRadius)
                return true;
        }
        return false;
    }

    private static void DrawBracket(DrawingHandleScreen handle, Vector2 point)
    {
        var left = point.X - BracketRadius;
        var right = point.X + BracketRadius;
        var top = point.Y - BracketRadius;
        var bottom = point.Y + BracketRadius;

        handle.DrawLine(new Vector2(left, top), new Vector2(left + BracketArm, top), LockColor);
        handle.DrawLine(new Vector2(left, top), new Vector2(left, top + BracketArm), LockColor);
        handle.DrawLine(new Vector2(right, top), new Vector2(right - BracketArm, top), LockColor);
        handle.DrawLine(new Vector2(right, top), new Vector2(right, top + BracketArm), LockColor);
        handle.DrawLine(new Vector2(left, bottom), new Vector2(left + BracketArm, bottom), LockColor);
        handle.DrawLine(new Vector2(left, bottom), new Vector2(left, bottom - BracketArm), LockColor);
        handle.DrawLine(new Vector2(right, bottom), new Vector2(right - BracketArm, bottom), LockColor);
        handle.DrawLine(new Vector2(right, bottom), new Vector2(right, bottom - BracketArm), LockColor);
        handle.DrawCircle(point, 2f, LockColor);
    }

    private static void DrawQualityRing(DrawingHandleScreen handle, Vector2 center, float quality, Color color)
    {
        const float radius = 10f;
        // full grey ring first, then a filled arc showing quality
        handle.DrawCircle(center, radius, Color.FromHex("#333333"), false);
        var start = -MathF.PI / 2f;                          // top
        var end = start + quality * MathF.Tau;             // clockwise
        const int steps = 20;
        var prev = center + new Vector2(MathF.Cos(start), MathF.Sin(start)) * radius;
        for (var i = 1; i <= steps; i++)
        {
            var t = start + (end - start) * i / steps;
            var next = center + new Vector2(MathF.Cos(t), MathF.Sin(t)) * radius;
            handle.DrawLine(prev, next, color);
            prev = next;
        }
    }
}
