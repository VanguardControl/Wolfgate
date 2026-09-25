using Content.Server.Administration.Logs;
using Content.Server.Storage.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server._WF.ShipAccess;

/// <summary>
/// Owns every edit to <see cref="WFShipAccessComponent"/>: registers the buyer at purchase, keeps each ship
/// access reader on the grid in step with the lock, and applies the console's access tab and verbs.
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
    /// Registers a bought ship: the owner from the purchaser's session or the ownership record, the mode
    /// from the grid's company and the lock from the cvar.
    /// </summary>
    public Entity<WFShipAccessComponent> SetupShip(EntityUid grid, EntityUid? purchaser)
    {
        var comp = EnsureComp<WFShipAccessComponent>(grid);
        var ship = new Entity<WFShipAccessComponent>(grid, comp);

        if (purchaser is { } buyer && _player.TryGetSessionByEntity(buyer, out var session))
        {
            comp.OwnerUserId = session.UserId;
            comp.OwnerName = Name(buyer);
        }
        else if (TryComp<ShipOwnershipComponent>(grid, out var ownership))
        {
            comp.OwnerUserId = ownership.OwnerUserId;
            if (_player.TryGetSessionById(ownership.OwnerUserId, out var owner))
                comp.OwnerName = owner.AttachedEntity is { } body ? Name(body) : owner.Name;
        }

        comp.Mode = _access.IsFactionGrid(grid, out _) ? WFShipAccessMode.Faction : WFShipAccessMode.Private;
        SetLocked(ship, _cfg.GetCVar(ShipAccessCVars.LockNewShips));
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Ship access set up on {ToPrettyString(grid):grid}: owner {comp.OwnerName} ({comp.OwnerUserId}), mode {comp.Mode}, locked {comp.Locked}");
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

    /// <summary>Adds a player to the allow list. Fails for NPCs, the owner and people already listed.</summary>
    public bool TryAddPerson(Entity<WFShipAccessComponent> ship, EntityUid person, string label = "")
    {
        if (!_player.TryGetSessionByEntity(person, out var session))
            return false;

        var userId = session.UserId;
        if (_access.IsOwner(ship, userId) || _access.TryGetEntry(ship.Comp, userId, out _))
            return false;

        ship.Comp.AllowList.Add(new WFShipAccessEntry { UserId = userId, Name = Name(person), Label = label });
        Dirty(ship);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(person):person} was added to the allow list of {ToPrettyString(ship.Owner):grid}");
        return true;
    }

    /// <summary>Takes a person off the allow list; false when they were not on it.</summary>
    public bool RemoveEntry(Entity<WFShipAccessComponent> ship, NetUserId userId)
    {
        if (!_access.TryGetEntry(ship.Comp, userId, out var entry))
            return false;

        ship.Comp.AllowList.Remove(entry);
        Dirty(ship);
        RemoveDoorPlayer(ship.Owner, userId);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{entry.Name} ({userId}) was removed from the allow list of {ToPrettyString(ship.Owner):grid}");
        return true;
    }

    /// <summary>Sets a listed person's builder flag; false when they are not listed.</summary>
    public bool SetBuilder(Entity<WFShipAccessComponent> ship, NetUserId userId, bool builder)
    {
        if (!_access.TryGetEntry(ship.Comp, userId, out var entry))
            return false;

        entry.Builder = builder;
        Dirty(ship);
        return true;
    }

    /// <summary>Empties the allow list and returns how many people were on it.</summary>
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

    /// <summary>Records the owner's account and display name.</summary>
    public void SetOwner(Entity<WFShipAccessComponent> ship, NetUserId userId, string name)
    {
        ship.Comp.OwnerUserId = userId;
        ship.Comp.OwnerName = name;
        Dirty(ship);
    }

    /// <summary>Console verb bridge: a guest swiped in at the console also joins the allow list.</summary>
    public void OnGuestAccessGranted(EntityUid grid, EntityUid user)
    {
        if (TryComp<WFShipAccessComponent>(grid, out var comp))
            TryAddPerson((grid, comp), user, Loc.GetString("ship-access-label-guest"));
    }

    /// <summary>Whether the actor may adopt an unowned ship: they hold its deed or the ownership record names them.</summary>
    public bool CanClaim(EntityUid grid, EntityUid actor, NetUserId userId)
    {
        return _access.HasDeedFor(actor, grid)
            || (TryComp<ShipOwnershipComponent>(grid, out var ownership) && ownership.OwnerUserId == userId);
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
