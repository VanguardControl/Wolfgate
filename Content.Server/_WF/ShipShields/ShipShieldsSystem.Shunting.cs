using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    /// <summary>Validates and applies one ship's shield power allocation.</summary>
    public bool SetWolfgateShieldShunt(EntityUid grid, float directionRadians, float concentration, float arcRadians)
    {
        if (!float.IsFinite(directionRadians) || !float.IsFinite(concentration) || !float.IsFinite(arcRadians) ||
            TerminatingOrDeleted(grid) || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return false;
        directionRadians = WFShipShieldShuntMath.NormalizeAngle(directionRadians);
        concentration = Math.Clamp(concentration, 0f, 1f);
        arcRadians = Math.Clamp(arcRadians, WFShipShieldShuntMath.MinimumArc, WFShipShieldShuntMath.FullArc);
        var allocation = EnsureComp<WFShipShieldShuntComponent>(grid);
        if (allocation.DirectionRadians == directionRadians && allocation.Concentration == concentration &&
            allocation.ArcRadians == arcRadians && allocation.Center == mapGrid.LocalAABB.Center)
            return true;
        allocation.DirectionRadians = directionRadians;
        allocation.Concentration = concentration;
        allocation.ArcRadians = arcRadians;
        allocation.Center = mapGrid.LocalAABB.Center;
        DirtyFields(grid, allocation, null, nameof(WFShipShieldShuntComponent.DirectionRadians),
            nameof(WFShipShieldShuntComponent.Concentration), nameof(WFShipShieldShuntComponent.ArcRadians),
            nameof(WFShipShieldShuntComponent.Center));
        if (TryComp<ShipShieldedComponent>(grid, out var shielded) &&
            TryComp<PhysicsComponent>(shielded.Shield, out var physics))
            CreateWolfgateShieldHull(shielded.Shield, grid, mapGrid, physics,
                Comp<WFShipShieldVisualsComponent>(shielded.Shield).Contours);
        return true;
    }

    /// <summary>Copies persistent ship allocation to the active shield.</summary>
    private WFShipShieldShuntComponent SyncWolfgateShieldShunt(EntityUid shield, EntityUid grid, MapGridComponent mapGrid)
    {
        var source = EnsureComp<WFShipShieldShuntComponent>(grid);
        source.Center = mapGrid.LocalAABB.Center;
        DirtyField(grid, source, nameof(WFShipShieldShuntComponent.Center));
        var target = EnsureComp<WFShipShieldShuntComponent>(shield);
        target.Center = source.Center;
        target.DirectionRadians = source.DirectionRadians;
        target.Concentration = source.Concentration;
        target.ArcRadians = source.ArcRadians;
        DirtyFields(shield, target, null, nameof(WFShipShieldShuntComponent.Center),
            nameof(WFShipShieldShuntComponent.DirectionRadians), nameof(WFShipShieldShuntComponent.Concentration),
            nameof(WFShipShieldShuntComponent.ArcRadians));
        return target;
    }

    /// <summary>Only fully diverted shields need angular filtering during broadphase checks.</summary>
    private bool IsWolfgateShieldUnprotectedProjectile(EntityUid shield, EntityUid projectile)
    {
        if (!TryComp<WFShipShieldShuntComponent>(shield, out var allocation) || allocation.Concentration < 1f ||
            allocation.ArcRadians >= WFShipShieldShuntMath.FullArc - 0.00001f)
            return false;
        return WolfgateShieldStrength(shield, projectile) <= 0f;
    }
    /// <summary>Returns the allocation at the actual hull boundary nearest a projectile.</summary>
    private float WolfgateShieldStrength(EntityUid shield, EntityUid projectile)
    {
        if (!TryComp<WFShipShieldShuntComponent>(shield, out var allocation) || allocation.Concentration <= 0f ||
            allocation.ArcRadians >= WFShipShieldShuntMath.FullArc - 0.00001f ||
            !TryComp<WFShipShieldVisualsComponent>(shield, out var visuals))
            return 1f;
        var local = Vector2.Transform(_transformSystem.GetWorldPosition(projectile), _transformSystem.GetInvWorldMatrix(shield));
        var position = WFShipShieldGeometry.ClosestPoint(visuals.Contours, local);
        return WFShipShieldShuntMath.StrengthMultiplier(position, allocation.Center, allocation.DirectionRadians,
            allocation.Concentration, allocation.ArcRadians);
    }
}
