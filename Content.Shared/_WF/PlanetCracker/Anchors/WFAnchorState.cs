using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Anchors;

/// <summary>Lifecycle of one gravity anchor, from crate contents to a locked drill head.</summary>
[Serializable, NetSerializable]
public enum WFAnchorState : byte
{
    /// <summary>Uncrated, unanchored, draggable.</summary>
    Loose,

    /// <summary>Wrenched down on a planet ground layer, no partner.</summary>
    Deployed,

    /// <summary>Partner found within the band on the same ground grid.</summary>
    Paired,

    /// <summary>Unattended drill running.</summary>
    Drilling,

    /// <summary>Drill finished; the pair is targetable by F4.</summary>
    Locked,

    /// <summary>Switched off after locking (F7 disconnect).</summary>
    Off,

    /// <summary>Destructible Breakage threshold crossed; repairable.</summary>
    Broken,
}
