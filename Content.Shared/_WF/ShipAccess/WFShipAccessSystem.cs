using System.Diagnostics.CodeAnalysis;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ShipAccess;

/// <summary>
/// Decides per-person ship access (owner, allow list, faction cards) for the ship access readers on
/// client and server. It never mutates; the server system does the edits.
/// </summary>
public sealed class WFShipAccessSystem : EntitySystem
{
    /// <summary>Tiles from the console within which a player can be added to the allow list.</summary>
    public const float AddRange = 3f;

    /// <summary>Digits in a ship or door code.</summary>
    public const int CodeLength = 4;

    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipAccessCheckEvent>(OnAccessCheck);
    }

    private void OnAccessCheck(ref WFShipAccessCheckEvent ev)
    {
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
    /// Whether a door rule admits the user at the door itself. Code and PlayersOrCode only admit the owner and
    /// listed people here; a code opens the door through the keypad, not the reader.
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
                return TryGetUserId(user, out var ownerId) && IsOwner(ship, ownerId);
            case WFDoorAccessRule.Players:
            case WFDoorAccessRule.PlayersOrCode:
                return TryGetUserId(user, out var userId) && (IsOwner(ship, userId) || rule.Players.Contains(userId));
            default:
                return !ship.Comp.Locked || IsAllowed(user, ship);
        }
    }

    /// <summary>Whether the rule lets a code open the door.</summary>
    public static bool TakesCode(WFDoorAccessRule rule)
    {
        return rule is WFDoorAccessRule.Code or WFDoorAccessRule.PlayersOrCode;
    }

    /// <summary>Whether the rule has a per-door player list.</summary>
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

    /// <summary>Whether the user is the owner, on the allow list or, in Faction mode, carries a card of the ship's company.</summary>
    public bool IsAllowed(EntityUid user, Entity<WFShipAccessComponent> ship)
    {
        if (TryGetUserId(user, out var userId) && (IsOwner(ship, userId) || TryGetEntry(ship.Comp, userId, out _)))
            return true;

        if (ship.Comp.Mode != WFShipAccessMode.Faction || !IsFactionGrid(ship, out var company))
            return false;

        foreach (var card in FindAccessibleIdCards(user))
        {
            if (TryComp<IdCardComponent>(card, out var idCard) && idCard.CompanyName == company)
                return true;
        }

        return false;
    }

    /// <summary>Account behind a player entity. NPCs have none; the client only resolves its own.</summary>
    public bool TryGetUserId(EntityUid user, out NetUserId userId)
    {
        if (_player.TryGetSessionByEntity(user, out var session))
        {
            userId = session.UserId;
            return true;
        }

        userId = default;
        return false;
    }

    /// <summary>Whether the account is the ship's recorded owner.</summary>
    public bool IsOwner(Entity<WFShipAccessComponent> ship, NetUserId userId)
    {
        return ship.Comp.HasOwner && ship.Comp.OwnerUserId == userId;
    }

    /// <summary>Finds the allow list entry for an account.</summary>
    public bool TryGetEntry(WFShipAccessComponent comp, NetUserId userId, [NotNullWhen(true)] out WFShipAccessEntry? entry)
    {
        foreach (var e in comp.AllowList)
        {
            if (e.UserId != userId)
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

    /// <summary>Whether any accessible card holds the deed for this grid.</summary>
    public bool HasDeedFor(EntityUid user, EntityUid grid)
    {
        foreach (var card in FindAccessibleIdCards(user))
        {
            if (TryComp<ShuttleDeedComponent>(card, out var deed) && deed.ShuttleUid == grid)
                return true;
        }

        return false;
    }
}
