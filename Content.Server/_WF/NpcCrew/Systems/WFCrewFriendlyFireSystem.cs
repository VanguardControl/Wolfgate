using Content.Server._WF.NpcCrew.Components;
using Content.Server.NPC.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Stops autonomous crew damage to allies before armor, wounds and retaliation process the hit.</summary>
public sealed class WFCrewFriendlyFireSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private WFCrewSecuritySystem _security = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private WFCrewEscortSystem _escorts = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedProjectileSystem _projectiles = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WoundDamageRoutingSystem _wounds = default!;

    /// <summary>How long one crew command attributes the weapon's automatic burst.</summary>
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(1);

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
        firing.Until = _timing.CurTime + BurstWindow;
    }

    private void OnShotAttempt(Entity<WFCrewShipFireComponent> ent, ref ShotAttemptedEvent args)
    {
        if (args.User != ent.Owner || !Commands(ent.Comp, ent.Owner))
            RemComp<WFCrewShipFireComponent>(ent);
    }

    /// <summary>A weapon stays attributed only to a living gunner of its grid, at its console, during its own burst.</summary>
    private bool Commands(WFCrewShipFireComponent firing, EntityUid weapon)
    {
        var gunner = firing.Crew;
        return _timing.CurTime <= firing.Until && !TerminatingOrDeleted(gunner) && !HasComp<ActorComponent>(gunner)
            && _mobs.IsAlive(gunner) && TryComp<WFCrewComponent>(gunner, out var crew)
            && _crew.HomeGrid(gunner, crew) is { } home && Transform(weapon).GridUid == home
            && (!TryComp<WFGunnerDutyComponent>(gunner, out var duty) || duty.AtConsole);
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
        if (args.Cancelled || !args.Damage.AnyPositive())
            return;
        if (args.Origin is not { } shooter)
        {
            ScaleBySkill(ent, ref args);
            return;
        }
        if (args.Tool is { } tool && ProtectedShot(tool, ent.Owner) || Protected(shooter, ent.Owner))
        {
            args.Cancelled = true;
            return;
        }
        ScaleBySkill(ent, ref args);
        if (args.Tool is { } beam && HasComp<HitscanBasicDamageComponent>(beam))
            ReportBeamHit(ent.Owner, shooter);
    }

    /// <summary>
    /// NPC crew are frailer than players and hit softer, by their skill. Applied once per hit, to the body: the
    /// wound system then routes the scaled damage to the parts, and its inner passes are left alone.
    /// </summary>
    private void ScaleBySkill(Entity<DamageableComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (!HasComp<MobStateComponent>(ent) || _wounds.IsRouting(ent))
            return;

        var scale = 1f;
        var victimIsCrew = TryComp<WFCrewComponent>(ent, out var victim) && !HasComp<ActorComponent>(ent);
        if (victimIsCrew)
            scale *= WFCrewSkills.Of(victim!.Skill).DamageTaken;
        else if (args.Origin is { } attacker && TryComp<WFCrewComponent>(attacker, out var dealer) && !HasComp<ActorComponent>(attacker))
            scale *= WFCrewSkills.Of(dealer.Skill).DamageDealt;

        if (scale != 1f)
            args.Damage = args.Damage * scale;
    }

    /// <summary>Reports ship hitscan damage to a hull the way ship-weapon projectile impacts are reported.</summary>
    private void ReportBeamHit(EntityUid target, EntityUid gun)
    {
        if (!TryComp<TransformComponent>(target, out var xform) || !xform.Anchored || xform.GridUid is not { } grid
            || !_alerts.IsShipWeapon(gun) || Transform(gun).GridUid is not { } attacker || attacker == grid)
            return;
        var ev = new WFCrewHullHitEvent(grid, attacker);
        RaiseLocalEvent(grid, ref ev, true);
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
        if (_crew.SameCrew(shooter, target))
            return true;
        var home = _crew.HomeGrid(shooter, crew);
        // Anyone who attacked this crew loses protection, whatever their company or faction.
        if (Attacked(shooter, crew, home, target))
            return false;
        EntityUid? alliedGrid = null;
        if (shipWeapon && home is { } ship
            && (HasComp<MapGridComponent>(target) ? target : Transform(target).GridUid) is { } grid)
        {
            var mob = HasComp<MobStateComponent>(target);
            if (grid == ship && !mob)
                return true;
            var hostile = _objectives.IsAttackTarget(ship, crew.Group, grid)
                || _alerts.IsHostileShip(ship, crew.Group, grid)
                || _security.IsHostileDockingTarget(ship, crew.Group, grid);
            // Own and formation ships shelter their hulls and everyone aboard who hasn't attacked this crew.
            if (!hostile && (grid == ship || _escorts.AreInFormation(ship, grid)))
                return true;
            if (!mob)
            {
                if (hostile)
                    return false;
                alliedGrid = grid;
            }
        }
        if (_security.IsAuthorized(shooter, target) || _factions.IsEntityFriendly(shooter, target))
            return true;
        return alliedGrid is { } friendlyGrid
            && (_security.IsAuthorized(shooter, friendlyGrid) || _factions.IsEntityFriendly(shooter, friendlyGrid));
    }

    /// <summary>Whether the target attacked this crewman or its crew, or was declared a hostile boarder.</summary>
    private bool Attacked(EntityUid shooter, WFCrewComponent crew, EntityUid? home, EntityUid target)
    {
        if (TryComp<NPCRetaliationComponent>(shooter, out var retaliation))
        {
            var memories = retaliation.AttackMemories;
            if (memories.TryGetValue(target, out var until) && _timing.CurTime < until)
                return true;
        }
        return home is { } grid && _alerts.IsSharedHostile(grid, crew.Group, target)
            || _security.IsHostileVisitor(shooter, target);
    }
}
