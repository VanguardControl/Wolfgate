using Content.Server._WF.NpcCrew.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Stops autonomous crew damage to allies before armor, wounds and retaliation process the hit.</summary>
public sealed class WFCrewFriendlyFireSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private WFCrewSecuritySystem _security = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private SharedProjectileSystem _projectiles = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnDamage,
            before: [typeof(WoundDamageRoutingSystem), typeof(InventorySystem), typeof(WFCrewSystem), typeof(WFRadioOperatorSystem)]);
        SubscribeLocalEvent<DamageableComponent, PreventCollideEvent>(OnCollision);
        SubscribeLocalEvent<WFCrewShipFireComponent, ShotAttemptedEvent>(OnShotAttempt);
        SubscribeLocalEvent<WFCrewShipFireComponent, AmmoShotEvent>(OnShot);
    }

    /// <summary>Only autonomous crew commands grant protection; a later manual command clears it.</summary>
    public void TrackWeapon(EntityUid weapon, EntityUid user)
    {
        if (!HasComp<WFCrewComponent>(user) || HasComp<ActorComponent>(user))
        {
            RemComp<WFCrewShipFireComponent>(weapon);
            return;
        }
        var firing = EnsureComp<WFCrewShipFireComponent>(weapon);
        firing.Crew = user;
        firing.Launched = false;
    }

    private void OnShotAttempt(Entity<WFCrewShipFireComponent> ent, ref ShotAttemptedEvent args)
    {
        if (args.User != ent.Owner || HasComp<ActorComponent>(ent.Comp.Crew))
            RemComp<WFCrewShipFireComponent>(ent);
    }

    private void OnShot(EntityUid uid, WFCrewShipFireComponent component, AmmoShotEvent args)
    {
        foreach (var round in args.FiredProjectiles)
        {
            if (!TryComp<ProjectileComponent>(round, out var projectile))
                continue;
            var firing = EnsureComp<WFCrewShipFireComponent>(round);
            firing.Crew = component.Crew;
            firing.Launched = true;
            _projectiles.SetShooter(round, projectile, component.Crew);
        }
    }

    private void OnCollision(Entity<DamageableComponent> ent, ref PreventCollideEvent args)
    {
        if (ProtectedShot(args.OtherEntity, ent.Owner)
            || TryComp<ProjectileComponent>(args.OtherEntity, out var projectile) && projectile.Shooter is { } shooter
                && Protected(shooter, ent.Owner))
            args.Cancelled = true;
    }

    private void OnDamage(Entity<DamageableComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive() || args.Origin is not { } shooter)
            return;
        if (args.Tool is { } tool && ProtectedShot(tool, ent.Owner) || Protected(shooter, ent.Owner))
            args.Cancelled = true;
    }

    /// <summary>Friendly-fire protection applies to autonomous crew, including allies outside their own group.</summary>
    public bool Protected(EntityUid shooter, EntityUid target)
    {
        return ProtectedShot(shooter, target)
            || !HasComp<ActorComponent>(shooter) && ProtectedCrew(shooter, target, shipWeapon: false);
    }

    private bool ProtectedShot(EntityUid source, EntityUid target)
    {
        return TryComp<WFCrewShipFireComponent>(source, out var firing)
            && (firing.Launched || !HasComp<ActorComponent>(firing.Crew))
            && ProtectedCrew(firing.Crew, target, shipWeapon: true);
    }

    private bool ProtectedCrew(EntityUid shooter, EntityUid target, bool shipWeapon)
    {
        if (!TryComp<WFCrewComponent>(shooter, out var crew))
            return false;
        EntityUid? alliedGrid = null;
        if (shipWeapon && !HasComp<MobStateComponent>(target) && Transform(shooter).GridUid is { } home
            && (HasComp<MapGridComponent>(target) ? target : Transform(target).GridUid) is { } grid)
        {
            if (grid == home)
                return true;
            if (EntityManager.System<WFCrewEscortSystem>().AreInFormation(home, grid))
                return true;
            if (_objectives.IsAttackTarget(home, crew.Group, grid)
                || EntityManager.System<WFCrewAlertSystem>().IsHostileShip(home, crew.Group, grid)
                || _security.IsHostileDockingTarget(home, crew.Group, grid))
                return false;
            alliedGrid = grid;
        }
        if (_security.IsAuthorized(shooter, target)
            || _factions.IsEntityFriendly(shooter, target) && !_security.IsHostileVisitor(shooter, target)
            || TryComp<WFCrewComponent>(target, out var other) && other.Group == crew.Group
                && Transform(target).GridUid == Transform(shooter).GridUid)
            return true;
        return alliedGrid is { } friendlyGrid
            && (_security.IsAuthorized(shooter, friendlyGrid) || _factions.IsEntityFriendly(shooter, friendlyGrid));
    }
}
