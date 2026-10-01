using Content.Shared._Crescent.ShipShields;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Components;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    /// <summary>Consumes a shield contact before a collision-triggered warhead can detonate.</summary>
    public bool AbsorbWolfgateTriggerCollision(EntityUid shield, EntityUid projectile)
    {
        if (!IsWolfgateShieldLive(shield) || !TryComp<PhysicsComponent>(shield, out var physics) || !physics.CanCollide ||
            !TryComp<ShipShieldComponent>(shield, out var field) || !_shipWeaponProjectileQuery.HasComponent(projectile) ||
            !_projectileQuery.TryGetComponent(projectile, out var component) || IsWolfgateShieldFriendlyProjectile(shield, projectile))
            return false;
        if (component.ProjectileSpent)
            return true;
        var contact = new ProjectileReflectAttemptEvent(projectile, component, false);
        OnWolfgateShieldContact(shield, field, ref contact);
        return component.ProjectileSpent;
    }
}
