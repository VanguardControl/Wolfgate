using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Cracker.BUI;

/// <summary>Target the console's owned anchor pair; the server re-checks alignment and the destination.</summary>
[Serializable, NetSerializable]
public sealed class WFCrackTargetMessage : BoundUserInterfaceMessage;

/// <summary>Drop the targeted pair; refused outside AnchorsLocked (design D23).</summary>
[Serializable, NetSerializable]
public sealed class WFCrackUntargetMessage : BoundUserInterfaceMessage;

/// <summary>Begin the crack. There is deliberately no abort counterpart (design D23).</summary>
[Serializable, NetSerializable]
public sealed class WFCrackBeginMessage : BoundUserInterfaceMessage;
