using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Shuttles.Components;

namespace Content.Server._WF.ShipAccess;

public sealed partial class WFShipAccessServerSystem
{
    /// <summary>The shuttle console's access tab: lock, add, remove, builder flags, door rules and codes.</summary>
    private void InitializeConsole()
    {
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<WFShipAccessSetLockedMessage>(OnSetLocked);
            subs.Event<WFShipAccessAddPlayerMessage>(OnAddPlayer);
            subs.Event<WFShipAccessRemoveMessage>(OnRemove);
            subs.Event<WFShipAccessSetBuilderMessage>(OnSetBuilder);
            subs.Event<WFShipAccessSetDoorRuleMessage>(OnSetDoorRule);
            subs.Event<WFShipAccessSetDoorPlayerMessage>(OnSetDoorPlayer);
            subs.Event<WFShipAccessRequestCodesMessage>(OnRequestCodes);
            subs.Event<WFShipAccessSetShipCodeMessage>(OnSetShipCode);
            subs.Event<WFShipAccessSetDoorCodeMessage>(OnSetDoorCode);
        });
    }

    private void OnSetLocked(Entity<ShuttleConsoleComponent> console, ref WFShipAccessSetLockedMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship))
            SetLocked(ship, args.Locked);
    }

    private void OnRemove(Entity<ShuttleConsoleComponent> console, ref WFShipAccessRemoveMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship))
            RemoveEntry(ship, args.Card);
    }

    private void OnSetBuilder(Entity<ShuttleConsoleComponent> console, ref WFShipAccessSetBuilderMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship))
            SetBuilder(ship, args.Card, args.Builder);
    }

    private void OnAddPlayer(Entity<ShuttleConsoleComponent> console, ref WFShipAccessAddPlayerMessage args)
    {
        var actor = args.Actor;
        if (!TryGetEditableShip(console, actor, out var ship) || !TryGetEntity(args.Target, out var target))
            return;

        if (!_transform.InRange(Transform(console.Owner).Coordinates, Transform(target.Value).Coordinates, WFShipAccessSystem.AddRange))
        {
            Popup(console, actor, "ship-access-add-out-of-range");
            return;
        }

        if (!_access.TryGetCard(target.Value, out _))
        {
            Popup(console, actor, "ship-access-add-no-card");
            return;
        }

        if (!TryAddPerson(ship, target.Value))
        {
            Popup(console, actor, "ship-access-add-already");
            return;
        }

        _popup.PopupEntity(Loc.GetString("ship-access-added", ("name", Name(target.Value))), console.Owner, actor);
    }

    /// <summary>
    /// The ship behind a console when the actor carries its deed. A ship bought before this module existed gets
    /// its component on the first edit; anyone without the deed gets the not-owner popup.
    /// </summary>
    private bool TryGetEditableShip(Entity<ShuttleConsoleComponent> console, EntityUid actor, out Entity<WFShipAccessComponent> ship)
    {
        ship = default;
        if (Transform(console.Owner).GridUid is not { } grid)
            return false;

        if (!_access.HasDeedFor(actor, grid))
        {
            Popup(console, actor, "ship-access-not-owner");
            return false;
        }

        ship = EnsureShip(grid);
        return true;
    }

    /// <summary>The component for a ship bought before ship access existed, keeping whatever the lock verb last set.</summary>
    private Entity<WFShipAccessComponent> EnsureShip(EntityUid grid)
    {
        if (TryComp<WFShipAccessComponent>(grid, out var comp))
            return (grid, comp);

        comp = EnsureComp<WFShipAccessComponent>(grid);
        comp.Mode = _access.IsFactionGrid(grid, out _) ? WFShipAccessMode.Faction : WFShipAccessMode.Private;
        comp.Locked = AnyReaderEnabled(grid);
        Dirty(grid, comp);
        return (grid, comp);
    }

    private bool AnyReaderEnabled(EntityUid grid)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<ShipAccessReaderComponent>(child, out var reader) && reader.Enabled)
                return true;
        }

        return false;
    }

    private void Popup(Entity<ShuttleConsoleComponent> console, EntityUid actor, string key)
    {
        _popup.PopupEntity(Loc.GetString(key), console.Owner, actor);
    }
}
