using Content.Server.Shuttles.Components;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Power;

namespace Content.Server._WF.ShipAccess;

public sealed partial class WFShipAccessServerSystem
{
    [Dependency] private SharedDoorSystem _door = default!;

    /// <summary>Per-door rules: set from the console, sealed doors bolted, door lists trimmed with the allow list.</summary>
    private void InitializeDoors()
    {
        SubscribeLocalEvent<WFDoorAccessRuleComponent, MapInitEvent>(OnRuleMapInit);
        SubscribeLocalEvent<WFDoorAccessRuleComponent, PowerChangedEvent>(OnRulePowerChanged);
    }

    /// <summary>A loaded door re-asserts its reader and bolts, since neither follows from the rule on its own.</summary>
    private void OnRuleMapInit(Entity<WFDoorAccessRuleComponent> door, ref MapInitEvent args)
    {
        TryEnsureReader(door);
        if (door.Comp.Rule == WFDoorAccessRule.Sealed)
            Seal(door);
    }

    /// <summary>Bolts need power, so a sealed door bolts, and an unsealed one unbolts, as soon as it is powered again.</summary>
    private void OnRulePowerChanged(Entity<WFDoorAccessRuleComponent> door, ref PowerChangedEvent args)
    {
        if (!args.Powered)
            return;

        if (door.Comp.Rule == WFDoorAccessRule.Sealed)
            Seal(door);
        else if (door.Comp.UnboltWhenPowered)
            Unseal(door);
    }

    /// <summary>Sets a door's rule. Sealing bolts and closes it; leaving Sealed unbolts it. False when nothing changed.</summary>
    public bool SetDoorRule(Entity<WFShipAccessComponent> ship, EntityUid door, WFDoorAccessRule rule)
    {
        var comp = EnsureComp<WFDoorAccessRuleComponent>(door);
        var old = comp.Rule;
        if (old == rule)
            return false;

        comp.Rule = rule;
        Dirty(door, comp);

        if (rule == WFDoorAccessRule.Sealed)
            Seal((door, comp));
        else if (old == WFDoorAccessRule.Sealed)
            Unseal((door, comp));

        TryEnsureReader(door);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Door {ToPrettyString(door):door} on {ToPrettyString(ship.Owner):grid} was set to rule {rule} (was {old})");
        return true;
    }

    /// <summary>Adds or removes an allow-listed card on a door's own list; false when it is not on the ship's list or nothing changed.</summary>
    public bool SetDoorPlayer(Entity<WFShipAccessComponent> ship, EntityUid door, EntityUid card, bool listed)
    {
        if (listed && !_access.TryGetEntry(ship.Comp, card, out _))
            return false;

        var comp = EnsureComp<WFDoorAccessRuleComponent>(door);
        if (listed == comp.Players.Contains(card))
            return false;

        if (listed)
            comp.Players.Add(card);
        else
            comp.Players.Remove(card);

        Dirty(door, comp);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Card {ToPrettyString(card):card} was {(listed ? "added to" : "removed from")} door {ToPrettyString(door):door} on {ToPrettyString(ship.Owner):grid}");
        return true;
    }

    /// <summary>Takes a card off every door list on the ship, after it left the allow list.</summary>
    private void RemoveDoorPlayer(EntityUid grid, EntityUid card)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<WFDoorAccessRuleComponent>(child, out var rule) && rule.Players.Remove(card))
                Dirty(child, rule);
        }
    }

    /// <summary>Empties every door list on the ship, after the allow list was cleared.</summary>
    private void ClearDoorPlayers(EntityUid grid)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<WFDoorAccessRuleComponent>(child, out var rule) || rule.Players.Count == 0)
                continue;

            rule.Players.Clear();
            Dirty(child, rule);
        }
    }

    /// <summary>Whether a door's rule needs its reader on even while the ship is unlocked.</summary>
    private bool ReaderShouldBeEnabled(WFShipAccessComponent access, EntityUid uid)
    {
        return access.Locked || (TryComp<WFDoorAccessRuleComponent>(uid, out var rule) && rule.Rule != WFDoorAccessRule.Default);
    }

    /// <summary>Closes and bolts a sealed door, adding bolts to a door that has none.</summary>
    private void Seal(Entity<WFDoorAccessRuleComponent> door)
    {
        door.Comp.UnboltWhenPowered = false;
        if (!TryComp<DoorBoltComponent>(door, out var bolt))
        {
            bolt = AddComp<DoorBoltComponent>(door);
            door.Comp.AddedBolt = true;
        }

        if (TryComp<DoorComponent>(door, out var doorComp) && doorComp.State == DoorState.Open && !_door.TryClose(door, doorComp))
            _door.StartClosing(door, doorComp);

        _door.SetBoltsDown((door, bolt), true);
    }

    /// <summary>
    /// Unbolts a door that leaves Sealed and takes the bolts away again if sealing added them. Bolts the door
    /// already had need power to come up, so without it the door is flagged to unbolt when power returns.
    /// </summary>
    private void Unseal(Entity<WFDoorAccessRuleComponent> door)
    {
        door.Comp.UnboltWhenPowered = false;
        if (!TryComp<DoorBoltComponent>(door, out var bolt))
            return;

        _door.SetBoltsDown((door, bolt), false);
        if (door.Comp.AddedBolt)
        {
            RemComp<DoorBoltComponent>(door);
            door.Comp.AddedBolt = false;
            return;
        }

        door.Comp.UnboltWhenPowered = _door.IsBolted(door, bolt);
    }

    /// <summary>
    /// Wipes what a seller set before a used ship goes to its next buyer: the access record, the ship code, and
    /// every door's code, rule and seal. A seal lifted without power keeps a bare rule until the bolts come up.
    /// </summary>
    public void ClearForResale(EntityUid grid)
    {
        RemComp<WFShipAccessComponent>(grid);
        RemComp<WFShipAccessCodeComponent>(grid);

        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            RemComp<WFDoorCodeComponent>(child);
            if (!TryComp<WFDoorAccessRuleComponent>(child, out var rule))
                continue;

            if (rule.Rule == WFDoorAccessRule.Sealed)
                Unseal((child, rule));

            if (!rule.UnboltWhenPowered)
            {
                RemComp<WFDoorAccessRuleComponent>(child);
                continue;
            }

            rule.Rule = WFDoorAccessRule.Default;
            rule.Players.Clear();
            rule.HasOwnCode = false;
            Dirty(child, rule);
        }
    }

    private void OnSetDoorRule(Entity<ShuttleConsoleComponent> console, ref WFShipAccessSetDoorRuleMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship) && TryGetShipDoor(console, ship, args.Door, args.Actor, out var door))
            SetDoorRule(ship, door, args.Rule);
    }

    private void OnSetDoorPlayer(Entity<ShuttleConsoleComponent> console, ref WFShipAccessSetDoorPlayerMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship) && TryGetShipDoor(console, ship, args.Door, args.Actor, out var door) && TryGetEntity(args.Card, out var card))
            SetDoorPlayer(ship, door, card.Value, args.Listed);
    }

    /// <summary>Resolves a console message's door and checks it is a door on the console's own grid. Firelocks take no rule.</summary>
    private bool TryGetShipDoor(Entity<ShuttleConsoleComponent> console, Entity<WFShipAccessComponent> ship, NetEntity netDoor, EntityUid actor, out EntityUid door)
    {
        if (TryGetEntity(netDoor, out var uid) && HasComp<DoorComponent>(uid) && !HasComp<FirelockComponent>(uid) && Transform(uid.Value).GridUid == ship.Owner)
        {
            door = uid.Value;
            return true;
        }

        door = default;
        Popup(console, actor, "ship-access-door-not-on-ship");
        return false;
    }
}
