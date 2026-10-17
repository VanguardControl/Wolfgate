using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ShipPa;

/// <summary>Raised on the grid (broadcast) after its situation code changed.</summary>
[ByRefEvent]
public readonly record struct ShipAlertCodeChangedEvent(
    EntityUid Grid,
    ProtoId<ShipAlertCodePrototype> Old,
    ProtoId<ShipAlertCodePrototype> New,
    EntityUid? User);

/// <summary>Raised on the grid (broadcast) when general quarters is sounded or secured.</summary>
[ByRefEvent]
public readonly record struct ShipGeneralQuartersChangedEvent(EntityUid Grid, bool Active, EntityUid? User);

/// <summary>What a <see cref="WFShipAlertChangedEvent"/> is about.</summary>
public enum WFShipAlertChange : byte
{
    Code,
    GeneralQuartersSounded,
    GeneralQuartersSecured,
    DistressSent,
    DistressCancelled,
    Announcement,
}

/// <summary>
/// Raised on the grid (broadcast) when its situation code, general quarters or distress call changes or someone
/// announces on its PA. <c>Text</c> is the code id or the announcement.
/// </summary>
[ByRefEvent]
public readonly record struct WFShipAlertChangedEvent(EntityUid Grid, WFShipAlertChange Change, string? Text);
