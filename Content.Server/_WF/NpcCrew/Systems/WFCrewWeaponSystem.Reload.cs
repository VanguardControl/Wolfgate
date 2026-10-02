using Content.Server._WF.NpcCrew.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Reloads from real equipment slots and falls back to another weapon when ammunition is exhausted.</summary>
public sealed partial class WFCrewWeaponSystem
{
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedGunSystem _guns = default!;

    private const string MagazineSlot = "gun_magazine";

    /// <summary>Returns the ammunition reported by the item's ordinary provider.</summary>
    public int AmmoCount(EntityUid item)
    {
        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(item, ref ev);
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
            || !IsDrawn(uid) || !HasComp<GunComponent>(gun) || AmmoCount(gun) > 0)
            return false;

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
                    if (TryComp<ChamberMagazineAmmoProviderComponent>(gun, out var chamber))
                    {
                        _guns.SetBoltClosed(gun, chamber, false, uid);
                        _guns.SetBoltClosed(gun, chamber, true, uid);
                    }
                    _hands.TrySelect(uid, gun);
                    return AmmoCount(gun) > 0;
                }
            }
            _inventory.TryEquip(uid, magazine, equipmentSlot, silent: true);
            _hands.TrySelect(uid, gun);
        }

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
