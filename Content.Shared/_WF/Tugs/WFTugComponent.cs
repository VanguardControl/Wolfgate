using System.Numerics;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Tugs;

/// <summary>A sit-on luggage tug: whoever buckles into it drives it, and it tows a train of trolleys along its path.</summary>
[RegisterComponent]
public sealed partial class WFTugComponent : Component
{
    /// <summary>Most trolleys the tug can tow.</summary>
    [DataField]
    public int MaxTrolleys = 5;

    /// <summary>Distance along the path from the tug's centre to the first trolley's.</summary>
    [DataField]
    public float HitchDistance = 1f;

    /// <summary>Distance along the path between one trolley's centre and the next.</summary>
    [DataField]
    public float TrolleySpacing = 1f;

    /// <summary>How far a trolley may stand from the back of the train to be coupled.</summary>
    [DataField]
    public float CoupleRange = 1.6f;

    /// <summary>Share of top speed lost per towed trolley.</summary>
    [DataField]
    public float SpeedPenaltyPerTrolley = 0.06f;

    /// <summary>Share of acceleration lost per towed trolley.</summary>
    [DataField]
    public float AccelerationPenaltyPerTrolley = 0.1f;

    /// <summary>Towed trolleys, nearest first.</summary>
    [ViewVariables]
    public List<EntityUid> Trolleys = new();

    /// <summary>The tug's past positions in its grid's frame, oldest first.</summary>
    [ViewVariables]
    public List<Vector2> Trail = new();

    /// <summary>The grid the trail is measured on.</summary>
    [ViewVariables]
    public EntityUid? TrailGrid;

    [ViewVariables]
    public float BaseMaxSpeed;

    [ViewVariables]
    public float BaseMaxReverseSpeed;

    [ViewVariables]
    public float BaseAcceleration;
}

/// <summary>A luggage trolley: couples behind a tug and carries one crate strapped to its bed.</summary>
[RegisterComponent]
public sealed partial class WFTugTrolleyComponent : Component
{
    [DataField]
    public string ContainerId = "wf-trolley-cargo";

    /// <summary>What can be strapped to the bed.</summary>
    [DataField]
    public EntityWhitelist? CargoWhitelist;

    /// <summary>Seconds to strap a crate on or take it off.</summary>
    [DataField]
    public float CargoDelay = 1.5f;

    /// <summary>The tug towing this trolley.</summary>
    [ViewVariables]
    public EntityUid? Tug;

    [ViewVariables]
    public ContainerSlot? Cargo;
}
