using System.Diagnostics.CodeAnalysis;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ShipAccess;

/// <summary>
/// Decides per-card ship access (the deed, the allow list, faction cards) for the ship access readers on
/// client and server, the way a normal airlock reads the ID cards a person carries. It never edits access
/// state; the server system does.
/// </summary>
public sealed class WFShipAccessSystem : EntitySystem
{
    /// <summary>Tiles from the console within which a person's card can be added to the allow list.</summary>
    public const float AddRange = 3f;

    /// <summary>Digits in a ship or door code.</summary>
    public const int CodeLength = 4;

    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedDoorSystem _door = default!;

    /// <summary>The person and door a correct keypad code is opening right now; the ship check waves exactly that through.</summary>
    private (EntityUid User, EntityUid Door)? _codeOpening;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipAccessCheckEvent>(OnAccessCheck);
    }

    /// <summary>
    /// Opens a door through the normal door path with only the ship check waived for this person, as a correct
    /// code does: power, welding, bolts and the door's own ID access still decide. Called by the server.
    /// </summary>
    public bool TryOpenByCode(EntityUid user, Entity<DoorComponent> door)
    {
        _codeOpening = (user, door.Owner);
        try
        {
            return _door.TryOpen(door, door.Comp, user);
        }
        finally
        {
            _codeOpening = null;
        }
    }

    private void OnAccessCheck(ref WFShipAccessCheckEvent ev)
    {
        if (_codeOpening is { } opening && opening.User == ev.User && opening.Door == ev.Target)
        {
            ev.Result = WFShipAccessResult.Allow;
            return;
        }

        if (!TryComp<WFShipAccessComponent>(ev.Grid, out var access))
            return;

        var ship = new Entity<WFShipAccessComponent>(ev.Grid, access);

        // A door with its own rule decides on its own, locked ship or not.
        if (TryComp<WFDoorAccessRuleComponent>(ev.Target, out var rule) && rule.Rule != WFDoorAccessRule.Default)
        {
            ev.Result = RuleAllows(ev.User, ship, rule) ? WFShipAccessResult.Allow : WFShipAccessResult.Deny;
            return;
        }

        if (!access.Locked || IsAllowed(ev.User, ship))
            ev.Result = WFShipAccessResult.Allow;
    }

    /// <summary>
    /// Whether a door rule admits the user at the door itself. Code and PlayersOrCode only admit the deed and
    /// listed cards here; a code opens the door through the keypad, not the reader.
    /// </summary>
    public bool RuleAllows(EntityUid user, Entity<WFShipAccessComponent> ship, WFDoorAccessRuleComponent rule)
    {
        switch (rule.Rule)
        {
            case WFDoorAccessRule.Public:
                return true;
            case WFDoorAccessRule.Sealed:
                return false;
            case WFDoorAccessRule.OwnerOnly:
            case WFDoorAccessRule.Code:
                return HasDeedFor(user, ship.Owner);
            case WFDoorAccessRule.Players:
            case WFDoorAccessRule.PlayersOrCode:
                foreach (var card in FindAccessibleIdCards(user))
                {
                    if (IsDeedFor(card, ship.Owner) || rule.Players.Contains(card))
                        return true;
                }

                return false;
            default:
                return !ship.Comp.Locked || IsAllowed(user, ship);
        }
    }

    /// <summary>Whether the rule lets a code open the door.</summary>
    public static bool TakesCode(WFDoorAccessRule rule)
    {
        return rule is WFDoorAccessRule.Code or WFDoorAccessRule.PlayersOrCode;
    }

    /// <summary>Whether the rule has a per-door card list.</summary>
    public static bool TakesPlayers(WFDoorAccessRule rule)
    {
        return rule is WFDoorAccessRule.Players or WFDoorAccessRule.PlayersOrCode;
    }

    /// <summary>Whether a code is exactly four digits.</summary>
    public static bool IsValidCode(string code)
    {
        if (code.Length != CodeLength)
            return false;

        foreach (var c in code)
        {
            if (c < '0' || c > '9')
                return false;
        }

        return true;
    }

    /// <summary>A door's rule, Default when it has no rule component.</summary>
    public WFDoorAccessRule GetRule(EntityUid door)
    {
        return TryComp<WFDoorAccessRuleComponent>(door, out var rule) ? rule.Rule : WFDoorAccessRule.Default;
    }

    /// <summary>Whether the user carries the deed, a listed card or, in Faction mode, a card of the ship's company.</summary>
    public bool IsAllowed(EntityUid user, Entity<WFShipAccessComponent> ship)
    {
        var cards = FindAccessibleIdCards(user);
        foreach (var card in cards)
        {
            if (IsDeedFor(card, ship.Owner) || TryGetEntry(ship.Comp, card, out _))
                return true;
        }

        if (ship.Comp.Mode != WFShipAccessMode.Faction || !IsFactionGrid(ship, out var company))
            return false;

        foreach (var card in cards)
        {
            if (TryComp<IdCardComponent>(card, out var idCard) && idCard.CompanyName == company)
                return true;
        }

        return false;
    }

    /// <summary>The card a person would swipe: the one in their active hand, else the one they wear. False when they carry none.</summary>
    public bool TryGetCard(EntityUid user, out EntityUid card)
    {
        if (_idCard.TryFindIdCard(user, out var idCard))
        {
            card = idCard.Owner;
            return true;
        }

        card = default;
        return false;
    }

    /// <summary>Finds the allow list entry for a card.</summary>
    public bool TryGetEntry(WFShipAccessComponent comp, EntityUid card, [NotNullWhen(true)] out WFShipAccessEntry? entry)
    {
        return TryGetEntry(comp, GetNetEntity(card), out entry);
    }

    /// <summary>Finds the allow list entry for a card by its network id.</summary>
    public bool TryGetEntry(WFShipAccessComponent comp, NetEntity card, [NotNullWhen(true)] out WFShipAccessEntry? entry)
    {
        foreach (var e in comp.AllowList)
        {
            if (e.Card != card)
                continue;

            entry = e;
            return true;
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// A grid belongs to a faction when its CompanyComponent names a real company. Purchase attaches one to
    /// nearly every ship, so "None" and empty both mean no faction.
    /// </summary>
    public bool IsFactionGrid(EntityUid grid, out ProtoId<CompanyPrototype> company)
    {
        company = default;
        if (!TryComp<CompanyComponent>(grid, out var comp) || string.IsNullOrEmpty(comp.CompanyName.Id) || comp.CompanyName == "None")
            return false;

        company = comp.CompanyName;
        return true;
    }

    /// <summary>ID cards in the user's hands or id slot, held directly or inside a PDA.</summary>
    public HashSet<EntityUid> FindAccessibleIdCards(EntityUid user)
    {
        var cards = new HashSet<EntityUid>();
        foreach (var item in _hands.EnumerateHeld(user))
            AddCard(item, cards);

        if (_inventory.TryGetSlotEntity(user, "id", out var idUid))
            AddCard(idUid.Value, cards);

        return cards;
    }

    private void AddCard(EntityUid item, HashSet<EntityUid> cards)
    {
        if (HasComp<IdCardComponent>(item))
            cards.Add(item);

        if (_idCard.TryGetIdCard(item, out var idCard))
            cards.Add(idCard.Owner);
    }

    /// <summary>Whether the card holds the deed for this grid.</summary>
    public bool IsDeedFor(EntityUid card, EntityUid grid)
    {
        return TryComp<ShuttleDeedComponent>(card, out var deed) && deed.ShuttleUid == grid;
    }

    /// <summary>Whether any accessible card holds the deed for this grid: the ship's owner, as far as its doors know.</summary>
    public bool HasDeedFor(EntityUid user, EntityUid grid)
    {
        foreach (var card in FindAccessibleIdCards(user))
        {
            if (IsDeedFor(card, grid))
                return true;
        }

        return false;
    }
}
