namespace Content.Server._WG.CustomMarkings;

/// <summary>Raised when an admin blocks art, so whatever else draws it (studio warps) takes it off bodies too.</summary>
[ByRefEvent]
public readonly record struct CustomMarkingArtBlockedEvent(string Hash);
