using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Anchors;

/// <summary>Raised when two anchors of the same owner pair up within the band.</summary>
[ByRefEvent]
public readonly record struct WFAnchorPairFormedEvent(EntityUid A, EntityUid B, float Distance);

/// <summary>Raised when a pair stops being a pair, for any reason; B is EntityUid.Invalid when the partner was already gone.</summary>
[ByRefEvent]
public readonly record struct WFAnchorPairDissolvedEvent(EntityUid A, EntityUid B);

/// <summary>Raised when an anchor's unattended drill starts.</summary>
[ByRefEvent]
public readonly record struct WFAnchorDrillStartedEvent(EntityUid Anchor);

/// <summary>Raised when an anchor's drill completes and it locks.</summary>
[ByRefEvent]
public readonly record struct WFAnchorDrillFinishedEvent(EntityUid Anchor);

/// <summary>Raised when an anchor crosses (either way) the damage threshold F4 uses to pause the crack.</summary>
[ByRefEvent]
public readonly record struct WFAnchorDamagedEvent(EntityUid Anchor, bool Damaged);

/// <summary>Raised when an anchor hits its Breakage threshold and must be repaired and re-locked.</summary>
[ByRefEvent]
public readonly record struct WFAnchorBrokenEvent(EntityUid Anchor);

/// <summary>Raised as an anchor entity terminates, after its pair has been dissolved.</summary>
[ByRefEvent]
public readonly record struct WFAnchorDestroyedEvent(EntityUid Anchor);

/// <summary>Cancellable: later features (F7) veto switching an anchor off outside the disconnect window.</summary>
public sealed class WFAnchorSwitchOffAttemptEvent(EntityUid anchor, EntityUid user) : CancellableEntityEventArgs
{
    /// <summary>The anchor being switched off.</summary>
    public EntityUid Anchor = anchor;

    /// <summary>Who pulled the switch.</summary>
    public EntityUid User = user;

    /// <summary>Localised refusal to show the user, set by whoever cancelled.</summary>
    public string? Reason;
}

/// <summary>Raised after an anchor is switched off.</summary>
[ByRefEvent]
public readonly record struct WFAnchorSwitchedOffEvent(EntityUid Anchor);

/// <summary>
/// Raised after a switched-off anchor is put back to Locked. Only a lapsed disconnect pairing window and the admin
/// command do this; there is no player-facing re-arm.
/// </summary>
[ByRefEvent]
public readonly record struct WFAnchorReArmedEvent(EntityUid Anchor);

/// <summary>Do-after for prying a crate open; must be shared and NetSerializable.</summary>
[Serializable, NetSerializable]
public sealed partial class WFAnchorUncrateDoAfterEvent : SimpleDoAfterEvent;
