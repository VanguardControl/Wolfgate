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
