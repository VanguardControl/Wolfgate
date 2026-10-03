using Content.Server._WF.NpcCrew.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Robust.Shared.Player;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Mobs.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared._WF.NpcCrew;
using System.Linq;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Reloads from real equipment slots and falls back to another weapon when ammunition is exhausted.</summary>
public sealed partial class WFCrewWeaponSystem
{
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private Robust.Shared.Timing.IGameTiming _timing = default!;

    private const string MagazineSlot = "gun_magazine";
    private const float LookupRange = 10f;
    private static readonly TimeSpan ThreatCacheTime = TimeSpan.FromSeconds(0.5);
    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Threat)> _threats = new();
    private float _reloadTimer;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _reloadTimer += frameTime;
        if (_reloadTimer < 0.5f)
            return;
        _reloadTimer = 0;
        foreach (var (mob, cached) in _threats)
        {
            if (_timing.CurTime >= cached.Until)
                _threats.Remove(mob);
        }
        var query = EntityQueryEnumerator<WFCrewWeaponComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (HasComp<ActorComponent>(uid) || !_mobs.IsAlive(uid))
                continue;
            if (TryComp<HTNComponent>(uid, out var plan)
                && plan.Blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager)
                && !CanEngage(uid, target))
            {
                EntityManager.System<HTNSystem>().Replan(plan);
                EntityManager.System<Content.Server.NPC.Systems.NPCSteeringSystem>().Unregister(uid);
                plan.Blackboard.Remove<EntityUid>("Target");
                RemCompDeferred<NPCRangedCombatComponent>(uid);
                RemCompDeferred<NPCMeleeCombatComponent>(uid);
            }
            if (HasLiveThreat(uid))
                TryReloadOrSwitch(uid);
            else
            {
                RemCompDeferred<NPCRangedCombatComponent>(uid);
                RemCompDeferred<NPCMeleeCombatComponent>(uid);
                TryHolster(uid);
            }
        }
    }

    /// <summary>Only living hostile targets in the crewman's current vision justify drawing or reloading.</summary>
    public bool HasLiveThreat(EntityUid uid)
    {
        var now = _timing.CurTime;
        if (_threats.TryGetValue(uid, out var cached) && now < cached.Until)
            return cached.Threat;
        var threat = PickTarget(uid) != null;
        _threats[uid] = (now + ThreatCacheTime, threat);
        return threat;
    }

    /// <summary>Hostiles within normal vision, plus the crew's shared targets and radio contacts.</summary>
    private IEnumerable<EntityUid> Candidates(EntityUid uid)
    {
        var range = TryComp<HTNComponent>(uid, out var htn)
            && htn.Blackboard.TryGetValue<float>("VisionRadius", out var vision, EntityManager) ? vision : LookupRange;
        // Alert-widened vision would turn the faction lookup into a scan of every faction member on the map.
        foreach (var target in _factions.GetNearbyHostiles(uid, Math.Min(range, LookupRange)))
            yield return target;
        if (!TryComp<WFCrewComponent>(uid, out var crew) || Transform(uid).GridUid is not { } grid)
            yield break;
        TryComp<NpcFactionMemberComponent>(uid, out var member);
        foreach (var target in EntityManager.System<WFCrewAlertSystem>().GetSharedHostiles(grid, crew.Group))
        {
            if (IsHostile(uid, member, target))
                yield return target;
        }
        foreach (var target in crew.RadioSightings.Keys)
        {
            if (IsHostile(uid, member, target))
                yield return target;
        }
    }

    /// <summary>The faction lookup's hostility rule for one known mob.</summary>
    private bool IsHostile(EntityUid uid, NpcFactionMemberComponent? member, EntityUid target)
    {
        if (target == uid || TerminatingOrDeleted(target) || _factions.IsIgnored(uid, target))
            return false;
        if (_factions.GetHostiles(uid).Contains(target))
            return true;
        return member != null && _factions.IsMemberOfAny(target, member.HostileFactions)
            && !_factions.IsEntityFriendly(uid, target);
    }

    /// <summary>Crew defend their assigned ship without pursuing targets across docking connections.</summary>
    public bool CanEngage(EntityUid uid, EntityUid target)
    {
        return !TerminatingOrDeleted(target) && _mobs.IsAlive(target)
            && Transform(uid).GridUid is { } grid && Transform(target).GridUid == grid
            && (!TryComp<WFCrewComponent>(uid, out var crew) || crew.Post is not { } post || post.EntityId == grid)
            && (!IsStationCrew(uid) || WasAttackedBy(uid, target))
            && (CanSee(uid, target) || EntityManager.System<WFCrewCommsSystem>().Knows(uid, target)
                || WasAttackedBy(uid, target));
    }

    /// <summary>Station operators leave their job only to defend themselves against a personal attacker.</summary>
    public bool IsStationCrew(EntityUid uid) => TryComp<WFCrewComponent>(uid, out var crew)
        && (crew.Duty is WFCrewDuties.Pilot or WFCrewDuties.Gunnery
            || crew.Role == WFCrewRoles.Captain || crew.Role == WFCrewRoles.RadioOperator);

    private bool WasAttackedBy(EntityUid uid, EntityUid target) => TryComp<NPCRetaliationComponent>(uid, out var retaliation)
        && retaliation.AttackMemories.Any(memory => memory.Key == target && _timing.CurTime < memory.Value);

    /// <summary>Walls and closed opaque doors conceal boarders; radio awareness does not extend eyesight.</summary>
    public bool CanSee(EntityUid uid, EntityUid target) => !TerminatingOrDeleted(target)
        && _interaction.InRangeUnobstructed(uid, target, 10f, Content.Shared.Physics.CollisionGroup.Opaque);

    /// <summary>Chooses a living hostile aboard the crewman's own ship.</summary>
    public EntityUid? PickTarget(EntityUid uid)
    {
        foreach (var target in Candidates(uid))
        {
            if (CanEngage(uid, target))
                return target;
        }
        return null;
    }

    /// <summary>Racks a loaded gun only when its chamber is empty or its bolt is open.</summary>
    private void ReadyChamber(EntityUid user, EntityUid gun)
    {
        if (!TryComp<ChamberMagazineAmmoProviderComponent>(gun, out var chamber))
            return;
        if (chamber.BoltClosed == false)
            _guns.SetBoltClosed(gun, chamber, true, user);
        else if (_slots.TryGetSlot(gun, "gun_chamber", out var slot)
                 && (!slot.HasItem || slot.Item is { } round && TryComp<CartridgeAmmoComponent>(round, out var cartridge) && cartridge.Spent))
        {
            if (chamber.BoltClosed != null)
            {
                _guns.SetBoltClosed(gun, chamber, false, user);
                _guns.SetBoltClosed(gun, chamber, true, user);
                return;
            }
            RaiseLocalEvent(gun, new UseInHandEvent(user));
        }
    }

    /// <summary>Returns the ammunition reported by the item's ordinary provider.</summary>
    public int AmmoCount(EntityUid item)
    {
        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(item, ref ev);
        if (HasComp<ChamberMagazineAmmoProviderComponent>(item)
            && _slots.TryGetSlot(item, "gun_chamber", out var chamber) && chamber.Item is { } round
            && TryComp<CartridgeAmmoComponent>(round, out var cartridge) && cartridge.Spent)
            return Math.Max(0, ev.Count - 1);
        return ev.Count;
    }

    private bool FindMagazineForOwner(EntityUid gun)
    {
        return Transform(gun).ParentUid is var owner && HasComp<WFCrewComponent>(owner)
            && FindMagazine(owner, gun, out _, out _);
    }

    private bool FindMagazine(EntityUid owner, EntityUid gun, out EntityUid magazine, out string equipmentSlot)
    {
        magazine = default;
        equipmentSlot = string.Empty;
        if (!_slots.TryGetSlot(gun, MagazineSlot, out var slot))
            return false;
        foreach (var name in WeaponSlots)
        {
            if (!_inventory.TryGetSlotEntity(owner, name, out var candidate) || candidate == gun
                || AmmoCount(candidate.Value) <= 0 || !_slots.CanInsert(gun, candidate.Value, owner, slot, swap: true))
                continue;
            magazine = candidate.Value;
            equipmentSlot = name;
            return true;
        }
        return false;
    }

    /// <summary>Reloads the held gun or holsters it and selects a usable fallback, leaving empty hands for melee.</summary>
    public bool TryReloadOrSwitch(EntityUid uid)
    {
        if (!TryComp<WFCrewWeaponComponent>(uid, out var weapon) || weapon.Drawn is not { } gun
            || !IsDrawn(uid) || !HasComp<GunComponent>(gun))
            return false;
        if (AmmoCount(gun) > 0)
        {
            ReadyChamber(uid, gun);
            return true;
        }

        if (FindMagazine(uid, gun, out var magazine, out var equipmentSlot)
            && TakeMagazine(uid, magazine, equipmentSlot))
        {
            _hands.TrySelect(uid, magazine);
            if (_slots.TryGetSlot(gun, MagazineSlot, out var slot)
                && (!slot.HasItem || _slots.TryEject(gun, slot, uid, out _)))
            {
                _interaction.InteractUsing(uid, magazine, gun, Transform(uid).Coordinates);
                if (slot.Item == magazine)
                {
                    EntityManager.System<WFCrewSpeechSystem>().Say(uid, "reload");
                    ReadyChamber(uid, gun);
                    _hands.TrySelect(uid, gun);
                    return AmmoCount(gun) > 0;
                }
            }
            _inventory.TryEquip(uid, magazine, equipmentSlot, silent: true);
            _hands.TrySelect(uid, gun);
        }

        RemCompDeferred<NPCRangedCombatComponent>(uid);
        if (TryComp<HTNComponent>(uid, out var htn))
            EntityManager.System<HTNSystem>().Replan(htn);
        EntityManager.System<WFCrewSpeechSystem>().Say(uid, "empty");
        TryHolster(uid);
        TryDraw(uid);
        return false;
    }

    private bool TakeMagazine(EntityUid uid, EntityUid magazine, string slot)
    {
        if (_hands.TryPickupAnyHand(uid, magazine, animateUser: false))
            return true;
        if (!_inventory.TryUnequip(uid, slot, silent: true))
            return false;
        if (_hands.TryPickupAnyHand(uid, magazine, animateUser: false))
            return true;
        _inventory.TryEquip(uid, magazine, slot, silent: true);
        return false;
    }
}
