using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipAccess;

/// <summary>
/// Raised by ShipAccessReaderSystem.HasShipAccess before Mono's deed rules. Allow short-circuits to true,
/// Deny to the denied popup and false, None continues.
/// </summary>
[ByRefEvent]
public record struct WFShipAccessCheckEvent(EntityUid User, EntityUid Target, EntityUid Grid)
{
    /// <summary>Set by the subscriber; None leaves the decision to the deed rules.</summary>
    public WFShipAccessResult Result;
}

/// <summary>Outcome of the per-person check; None leaves the decision to the deed rules.</summary>
public enum WFShipAccessResult : byte
{
    None,
    Allow,
    Deny,
}

/// <summary>The owner flips the ship lock from the console's access tab.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessSetLockedMessage : BoundUserInterfaceMessage
{
    public bool Locked;

    public WFShipAccessSetLockedMessage(bool locked)
    {
        Locked = locked;
    }
}

/// <summary>The owner adds a humanoid standing near the console to the allow list.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessAddPlayerMessage : BoundUserInterfaceMessage
{
    public NetEntity Target;

    public WFShipAccessAddPlayerMessage(NetEntity target)
    {
        Target = target;
    }
}

/// <summary>The owner removes a person from the allow list.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessRemoveMessage : BoundUserInterfaceMessage
{
    public NetUserId UserId;

    public WFShipAccessRemoveMessage(NetUserId userId)
    {
        UserId = userId;
    }
}

/// <summary>The owner marks or unmarks a listed person as a builder.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessSetBuilderMessage : BoundUserInterfaceMessage
{
    public NetUserId UserId;
    public bool Builder;

    public WFShipAccessSetBuilderMessage(NetUserId userId, bool builder)
    {
        UserId = userId;
        Builder = builder;
    }
}

/// <summary>A deed holder adopts ownership of a ship that has no owner yet.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessClaimMessage : BoundUserInterfaceMessage
{
}

/// <summary>The owner sets a door's rule from the door diagram.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessSetDoorRuleMessage : BoundUserInterfaceMessage
{
    public NetEntity Door;
    public WFDoorAccessRule Rule;

    public WFShipAccessSetDoorRuleMessage(NetEntity door, WFDoorAccessRule rule)
    {
        Door = door;
        Rule = rule;
    }
}

/// <summary>The owner ticks or unticks an allow-listed person on a door's own list.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessSetDoorPlayerMessage : BoundUserInterfaceMessage
{
    public NetEntity Door;
    public NetUserId UserId;
    public bool Listed;

    public WFShipAccessSetDoorPlayerMessage(NetEntity door, NetUserId userId, bool listed)
    {
        Door = door;
        UserId = userId;
        Listed = listed;
    }
}

/// <summary>The owner asks the console for the ship and door codes; the server answers with <see cref="WFShipAccessCodesEvent"/>.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessRequestCodesMessage : BoundUserInterfaceMessage
{
}

/// <summary>The owner sets (4 digits) or clears (null) the ship code.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessSetShipCodeMessage : BoundUserInterfaceMessage
{
    public string? Code;

    public WFShipAccessSetShipCodeMessage(string? code)
    {
        Code = code;
    }
}

/// <summary>The owner sets (4 digits) or clears (null) a door's own code.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessSetDoorCodeMessage : BoundUserInterfaceMessage
{
    public NetEntity Door;
    public string? Code;

    public WFShipAccessSetDoorCodeMessage(NetEntity door, string? code)
    {
        Door = door;
        Code = code;
    }
}

/// <summary>Sent to the owner's client only: the ship's codes and the keypad alert counts. Never broadcast.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessCodesEvent : EntityEventArgs
{
    public NetEntity Grid;
    public string? ShipCode;
    public Dictionary<NetEntity, string> DoorCodes;
    public int Misses;
    public int LockedOut;

    public WFShipAccessCodesEvent(NetEntity grid, string? shipCode, Dictionary<NetEntity, string> doorCodes, int misses, int lockedOut)
    {
        Grid = grid;
        ShipCode = shipCode;
        DoorCodes = doorCodes;
        Misses = misses;
        LockedOut = lockedOut;
    }
}

/// <summary>Sent to the owner's client after a keypad miss: failed attempts this round and people locked out now.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessCodeAlertEvent : EntityEventArgs
{
    public NetEntity Grid;
    public int Misses;
    public int LockedOut;

    public WFShipAccessCodeAlertEvent(NetEntity grid, int misses, int lockedOut)
    {
        Grid = grid;
        Misses = misses;
        LockedOut = lockedOut;
    }
}

/// <summary>The server tells one client to open the keypad for a door, after its Enter Code verb.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessOpenKeypadEvent : EntityEventArgs
{
    public NetEntity Door;

    public WFShipAccessOpenKeypadEvent(NetEntity door)
    {
        Door = door;
    }
}

/// <summary>A client submits a keypad code for a door.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessSubmitCodeMessage : EntityEventArgs
{
    public NetEntity Door;
    public string Code;

    public WFShipAccessSubmitCodeMessage(NetEntity door, string code)
    {
        Door = door;
        Code = code;
    }
}
