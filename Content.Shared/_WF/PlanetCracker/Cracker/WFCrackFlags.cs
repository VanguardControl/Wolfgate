using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>Preconditions that have failed mid-crack; what the grace countdown is counting down for.</summary>
[Flags, Serializable, NetSerializable]
public enum WFCrackFailure : byte
{
    /// <summary>Nothing failing.</summary>
    None = 0,

    /// <summary>No centrifuge on the hull, or its rotor has fallen below full.</summary>
    Centrifuge = 1 << 0,

    /// <summary>Fewer projectors on the hull than RequiredProjectors.</summary>
    ProjectorsShort = 1 << 1,

    /// <summary>At least one projector has lost power.</summary>
    ProjectorPower = 1 << 2,

    /// <summary>At least one projector is past its Breakage threshold.</summary>
    ProjectorBroken = 1 << 3,
}

/// <summary>
/// Why BEGIN CRACK is refused right now. One flag per distinct fault and one locale key per flag, because the
/// console hover list is the crew's only explanation of a greyed button (design section 263).
/// </summary>
[Flags, Serializable, NetSerializable]
public enum WFCrackBlocker : ushort
{
    /// <summary>Nothing blocking; the crack can begin.</summary>
    None = 0,

    /// <summary>The cracker is not in AnchorsLocked.</summary>
    WrongState = 1 << 0,

    /// <summary>No owned anchor pair to work with.</summary>
    NoPair = 1 << 1,

    /// <summary>A pair exists but has not been targeted.</summary>
    NotTargeted = 1 << 2,

    /// <summary>The berth centre is further from the cut circle centre than AlignTolerance.</summary>
    NotAligned = 1 << 3,

    /// <summary>Another grid sits over the hull's destination footprint.</summary>
    Obstructed = 1 << 4,

    /// <summary>The hull is in a CE grid network of two or more grids, which would undo the snap (D-J).</summary>
    InGridNetwork = 1 << 5,

    /// <summary>No centrifuge on the hull at all.</summary>
    CentrifugeMissing = 1 << 6,

    /// <summary>The centrifuge is on the hull but its rotor is not at full.</summary>
    CentrifugeNotFull = 1 << 7,

    /// <summary>Fewer projectors on the hull than RequiredProjectors.</summary>
    ProjectorsShort = 1 << 8,

    /// <summary>At least one projector has no power.</summary>
    ProjectorsUnpowered = 1 << 9,

    /// <summary>At least one projector is past its Breakage threshold.</summary>
    ProjectorsBroken = 1 << 10,

    /// <summary>This planet has already been cracked; the only permanent fault in the list.</summary>
    PlanetCracked = 1 << 11,
}
