using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.LightFlicker;

/// <summary>Raised when a multitool finishes repairing a damaged ballast.</summary>
[Serializable, NetSerializable]
public sealed partial class BallastRepairDoAfterEvent : SimpleDoAfterEvent;
