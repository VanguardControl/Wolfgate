using System.Numerics;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>Raised broadcast by WFCrackerSystem.SetState after the component is dirtied; never fires on a no-op.</summary>
[ByRefEvent]
public readonly record struct WFCrackStateChangedEvent(EntityUid Cracker, WFCrackState Old, WFCrackState New);

/// <summary>Raised when a crack finishes cutting.</summary>
[ByRefEvent]
public readonly record struct WFCrackCompletedEvent(
    EntityUid Cracker,
    EntityUid AnchorA,
    EntityUid AnchorB,
    Vector2 CentreXY,
    float Radius,
    EntityUid GroundMap);

/// <summary>Raised as the hull is pushed into transit, so the chunk joins the same fall at a distinct progress.</summary>
[ByRefEvent]
public readonly record struct WFCrackerFallingEvent(EntityUid Cracker);

/// <summary>Raised broadcast as the evacuation runs out so the chunk system drops the chunk.</summary>
[ByRefEvent]
public readonly record struct WFCrackerReleasingEvent(EntityUid Cracker);
