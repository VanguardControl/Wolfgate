using Content.Server._WF.NpcCrew.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Stops autonomous crew damage to allies before armor, wounds and retaliation process the hit.</summary>
public sealed class WFCrewFriendlyFireSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private WFCrewSecuritySystem _security = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnDamage,
            before: [typeof(WoundDamageRoutingSystem), typeof(InventorySystem), typeof(WFCrewSystem), typeof(WFRadioOperatorSystem)]);
        SubscribeLocalEvent<DamageableComponent, PreventCollideEvent>(OnCollision);
    }

    private void OnCollision(Entity<DamageableComponent> ent, ref PreventCollideEvent args)
    {
        if (TryComp<ProjectileComponent>(args.OtherEntity, out var projectile) && projectile.Shooter is { } shooter
            && Protected(shooter, ent.Owner))
            args.Cancelled = true;
    }

    private void OnDamage(Entity<DamageableComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive() || args.Origin is not { } shooter)
            return;
        if (Protected(shooter, ent.Owner))
            args.Cancelled = true;
    }

    /// <summary>Friendly-fire protection applies to autonomous crew, including allies outside their own group.</summary>
    public bool Protected(EntityUid shooter, EntityUid target)
    {
        return TryComp<WFCrewComponent>(shooter, out var crew) && !HasComp<ActorComponent>(shooter)
            && (_security.IsAuthorized(shooter, target)
                || _factions.IsEntityFriendly(shooter, target) && !_security.IsHostileVisitor(shooter, target)
                || TryComp<WFCrewComponent>(target, out var other) && other.Group == crew.Group
                && Transform(target).GridUid == Transform(shooter).GridUid);
    }
}
