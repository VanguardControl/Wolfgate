using Content.Shared._Shitmed.Targeting;
using Content.Shared.MedicalScanner;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Autodoc;

[Serializable, NetSerializable]
public enum AutodocUiKey : byte
{
    Key,
}

/// <summary>
/// Everything the pod window draws. The diagnostics field is the analyzer's own scan message, so the window
/// can feed the analyzer's doll and wound panel without a second payload type.
/// </summary>
[Serializable, NetSerializable]
public sealed class AutodocBuiState : BoundUserInterfaceState
{
    public HealthAnalyzerScannedUserMessage? Diagnostics;
    public List<AutodocProcedureEntry> Available = new();
    public List<AutodocQueueEntry> Queue = new();
    public List<AutodocReservoirEntry> Reservoir = new();
    public AutodocState State;
    public string Status = string.Empty;
    public string? CurrentStep;
    public float Progress;
    /// <summary>Game time the current step ends at, so the client can run the bar between updates.</summary>
    public TimeSpan StepEnds;
    public float StepLength;
    public string? DiskProgram;
    public bool DefibModule;
    public bool AutofixModule;
    /// <summary>The autofix module is fitted and switched on, so the pod plans and starts by itself.</summary>
    public bool Auto;
    public bool SelfService;
    public bool Anaesthesia;
    public bool Occupied;
    public string? TrayItem;
    /// <summary>What S.A.M. said last, for the terminal line.</summary>
    public string? LastLine;
    /// <summary>Waiting on clothing rather than on the tray, which is what the CUT button is for.</summary>
    public bool ClothingBlocked;
    /// <summary>The pod is topping the occupant's blood up out of the reservoir.</summary>
    public bool Transfusing;
}

/// <summary>A procedure the occupant's current condition allows on one part.</summary>
[Serializable, NetSerializable]
public readonly record struct AutodocProcedureEntry(EntProtoId Surgery, TargetBodyPart Part, bool Known);

/// <summary>A queued procedure with its live requirement lines.</summary>
[Serializable, NetSerializable]
public readonly record struct AutodocQueueEntry(EntProtoId Surgery, TargetBodyPart Part, List<string> Requirements);

[Serializable, NetSerializable]
public readonly record struct AutodocReservoirEntry(string Name, float Volume, float Max, bool Usable);

[Serializable, NetSerializable]
public sealed class AutodocQueueAddMessage(EntProtoId surgery, TargetBodyPart part) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Surgery = surgery;
    public readonly TargetBodyPart Part = part;
}

[Serializable, NetSerializable]
public sealed class AutodocQueueRemoveMessage(int index) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
}

[Serializable, NetSerializable]
public sealed class AutodocQueueMoveMessage(int index, bool up) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
    public readonly bool Up = up;
}

public enum AutodocControl : byte
{
    Start,
    Pause,
    Abort,
    Eject,

    /// <summary>Fill the queue from the triage plan. In self-service it also starts: the FIX ME button.</summary>
    Plan,

    /// <summary>Toggle the autofix module's automatic mode.</summary>
    Auto,

    /// <summary>Cut and destroy whatever is in the way of the skin.</summary>
    CutClothing,
}

[Serializable, NetSerializable]
public sealed class AutodocControlMessage(AutodocControl control) : BoundUserInterfaceMessage
{
    public readonly AutodocControl Control = control;
}

[Serializable, NetSerializable]
public sealed class AutodocAnaesthesiaMessage(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
}
