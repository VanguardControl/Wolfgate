using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Tugs;

/// <summary>Ends the do-after of strapping a crate to a trolley.</summary>
[Serializable, NetSerializable]
public sealed partial class WFTrolleyLoadDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Ends the do-after of taking a trolley's crate off.</summary>
[Serializable, NetSerializable]
public sealed partial class WFTrolleyUnloadDoAfterEvent : SimpleDoAfterEvent;
