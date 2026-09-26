using Content.Server.Administration.Logs;
using Content.Server.Storage.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Components;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;

namespace Content.Server._WF.ShipAccess;

/// <summary>
/// Owns every edit to <see cref="WFShipAccessComponent"/>: sets a ship up at purchase, keeps each ship access
/// reader on the grid in step with the lock, and applies the console's access tab and verbs. The allow list
/// holds ID cards; ownership is the deed card and is never recorded here.
/// </summary>
public sealed partial class WFShipAccessServerSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WFShipAccessSystem _access = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>(OnShipPurchased);
        SubscribeLocalEvent<DoorComponent, EntParentChangedMessage>(OnDoorParentChanged);
        SubscribeLocalEvent<DoorComponent, AnchorStateChangedEvent>(OnDoorAnchorChanged);
        SubscribeLocalEvent<EntityStorageComponent, EntParentChangedMessage>(OnStorageParentChanged);
        SubscribeLocalEvent<EntityStorageComponent, AnchorStateChangedEvent>(OnStorageAnchorChanged);
        InitializeConsole();
        InitializeDoors();
        InitializeCodes();
    }

    private void OnShipPurchased(ShipyardShuttlePurchaseEvent args)
    {
        SetupShip(args.Shuttle, args.Purchaser);
    }

    /// <summary>
    /// Sets a bought ship up: the buyer's name for display, the mode from the grid's company and the lock from
    /// the cvar. Ownership itself is the deed card the shipyard hands out.
    /// </summary>
    public Entity<WFShipAccessComponent> SetupShip(EntityUid grid, EntityUid? purchaser)
    {
        var comp = EnsureComp<WFShipAccessComponent>(grid);
        var ship = new Entity<WFShipAccessComponent>(grid, comp);

        if (purchaser is { } buyer)
            comp.OwnerName = Name(buyer);
        else if (TryComp<ShuttleDeedComponent>(grid, out var deed) && !string.IsNullOrEmpty(deed.ShuttleOwner))
            comp.OwnerName = deed.ShuttleOwner;

        comp.Mode = _access.IsFactionGrid(grid, out _) ? WFShipAccessMode.Faction : WFShipAccessMode.Private;
        SetLocked(ship, _cfg.GetCVar(ShipAccessCVars.LockNewShips));
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Ship access set up on {ToPrettyString(grid):grid}: registered to {comp.OwnerName}, mode {comp.Mode}, locked {comp.Locked}");
        return ship;
    }

    /// <summary>Sets the lock and mirrors it into every ship access reader on the grid; a door with its own rule stays enabled.</summary>
    public void SetLocked(Entity<WFShipAccessComponent> ship, bool locked)
    {
        ship.Comp.Locked = locked;
        Dirty(ship);

        var children = Transform(ship.Owner).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<ShipAccessReaderComponent>(child, out var reader))
                continue;

            var enabled = ReaderShouldBeEnabled(ship.Comp, child);
            if (reader.Enabled == enabled)
                continue;

            reader.Enabled = enabled;
            Dirty(child, reader);
        }
    }

    /// <summary>Console verb bridge: false when the grid has no ship access, so the caller keeps its old sweep.</summary>
    public bool TrySetLocked(EntityUid? grid, bool locked)
    {
        if (grid is not { } uid || !TryComp<WFShipAccessComponent>(uid, out var comp))
            return false;

        SetLocked((uid, comp), locked);
        return true;
    }

    /// <summary>Adds the card a person carries to the allow list. Fails when they carry none, it is the deed, or it is listed already.</summary>
    public bool TryAddPerson(Entity<WFShipAccessComponent> ship, EntityUid person, string label = "")
    {
        return _access.TryGetCard(person, out var card) && TryAddCard(ship, card, Name(person), label);
    }

    /// <summary>Adds an ID card to the allow list under the name on it, or the holder's name when the card is blank.</summary>
    public bool TryAddCard(Entity<WFShipAccessComponent> ship, EntityUid card, string holderName, string label = "")
    {
        if (_access.IsDeedFor(card, ship.Owner) || _access.TryGetEntry(ship.Comp, card, out _))
            return false;

        var name = TryComp<IdCardComponent>(card, out var idCard) && !string.IsNullOrEmpty(idCard.FullName) ? idCard.FullName : holderName;
        ship.Comp.AllowList.Add(new WFShipAccessEntry { Card = GetNetEntity(card), Name = name, Label = label });
        Dirty(ship);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Card {ToPrettyString(card):card} ({name}) was added to the allow list of {ToPrettyString(ship.Owner):grid}");
        return true;
    }

    /// <summary>Takes a card off the allow list; false when it was not on it.</summary>
    public bool RemoveEntry(Entity<WFShipAccessComponent> ship, NetEntity card)
    {
        if (!_access.TryGetEntry(ship.Comp, card, out var entry))
            return false;

        ship.Comp.AllowList.Remove(entry);
        Dirty(ship);
        if (TryGetEntity(card, out var uid))
            RemoveDoorPlayer(ship.Owner, uid.Value);

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Card {card} ({entry.Name}) was removed from the allow list of {ToPrettyString(ship.Owner):grid}");
        return true;
    }

    /// <summary>Sets a listed card's builder flag; false when it is not listed.</summary>
    public bool SetBuilder(Entity<WFShipAccessComponent> ship, NetEntity card, bool builder)
    {
        if (!_access.TryGetEntry(ship.Comp, card, out var entry))
            return false;

        entry.Builder = builder;
        Dirty(ship);
        return true;
    }

    /// <summary>Empties the allow list and returns how many cards were on it.</summary>
    public int ClearAllowList(Entity<WFShipAccessComponent> ship)
    {
        var count = ship.Comp.AllowList.Count;
        if (count == 0)
            return 0;

        ship.Comp.AllowList.Clear();
        Dirty(ship);
        ClearDoorPlayers(ship.Owner);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"The allow list of {ToPrettyString(ship.Owner):grid} was cleared ({count} entries)");
        return count;
    }

    /// <summary>Console verb bridge: 0 when the grid has no ship access.</summary>
    public int ClearAllowList(EntityUid grid)
    {
        return TryComp<WFShipAccessComponent>(grid, out var comp) ? ClearAllowList((grid, comp)) : 0;
    }

    /// <summary>Console verb bridge: a guest swiped in at the console also joins the allow list.</summary>
    public void OnGuestAccessGranted(EntityUid grid, EntityUid user)
    {
        if (TryComp<WFShipAccessComponent>(grid, out var comp))
            TryAddPerson((grid, comp), user, Loc.GetString("ship-access-label-guest"));
    }

    private void OnDoorParentChanged(Entity<DoorComponent> ent, ref EntParentChangedMessage args)
    {
        TryEnsureReader(ent);
    }

    private void OnDoorAnchorChanged(Entity<DoorComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Detaching)
            TryEnsureReader(ent);
    }

    private void OnStorageParentChanged(Entity<EntityStorageComponent> ent, ref EntParentChangedMessage args)
    {
        TryEnsureReader(ent);
    }

    private void OnStorageAnchorChanged(Entity<EntityStorageComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Detaching)
            TryEnsureReader(ent);
    }

    /// <summary>Gives a door or locker on a ship with access control a reader that mirrors the lock, or the door's own rule.</summary>
    private void TryEnsureReader(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;

        if (Transform(uid).GridUid is not { } grid || !TryComp<WFShipAccessComponent>(grid, out var access))
            return;

        var reader = EnsureComp<ShipAccessReaderComponent>(uid);
        var enabled = ReaderShouldBeEnabled(access, uid);
        if (reader.Enabled == enabled)
            return;

        reader.Enabled = enabled;
        Dirty(uid, reader);
    }
}
