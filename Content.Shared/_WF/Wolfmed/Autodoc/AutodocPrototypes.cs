using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>
/// One S.A.M. voice. Every line has exactly one ogg and exactly one transcript, so the audio and the chat
/// line can never disagree; <see cref="Events"/> names the variants an event may pick between.
/// </summary>
[Prototype("autodocVoice")]
public sealed partial class AutodocVoicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public Dictionary<string, AutodocVoiceLine> Lines = new();

    [DataField(required: true)]
    public Dictionary<AutodocVoiceEvent, List<string>> Events = new();
}

[DataDefinition]
public sealed partial class AutodocVoiceLine
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    [DataField(required: true)]
    public LocId Message;

    /// <summary>How the line behaves in the pod's voice queue. Generated with the line, never hand written.</summary>
    [DataField]
    public AutodocVoicePriority Priority = AutodocVoicePriority.Info;
}

/// <summary>
/// What a line does when the pod is already talking. Only one line is ever audible at a time, so every id
/// carries the rule for its own interruption.
/// </summary>
public enum AutodocVoicePriority : byte
{
    /// <summary>Small talk. Only ever starts into silence, never queues.</summary>
    Chatter,

    /// <summary>Announces a step. Dropped outright if anything is playing or waiting; the next step speaks instead.</summary>
    Step,

    /// <summary>Waits its turn in the queue.</summary>
    Info,

    /// <summary>Cuts off whatever is playing and speaks now.</summary>
    Urgent,
}

/// <summary>Everything S.A.M. has something to say about.</summary>
public enum AutodocVoiceEvent : byte
{
    Boot,
    Greeting,
    SelfService,
    OperatorStart,
    QueueEmpty,
    NoOccupant,
    Anaesthetic,
    NoAnaesthetic,
    StepIncision,
    StepRetract,
    StepClamp,
    StepCauterise,
    StepSaw,
    StepDrill,
    StepSetBone,
    StepBoneGel,
    StepSuture,
    StepClose,
    StepRemovePart,
    StepAttachPart,
    StepRemoveOrgan,
    StepInsertOrgan,
    StepEmbedded,
    StepTend,
    StepRelocate,
    StepWeld,
    StepWrench,
    StepWire,
    StepAmputate,
    StepEvisceration,
    StepCavity,
    StepGeneric,
    RequirePart,
    RequireOrgan,
    RequireItem,
    WrongItem,
    ItemAccepted,
    ReagentMissing,
    ReagentIgnored,
    Paused,
    Resumed,
    Aborted,
    EmergencyEject,
    PowerLost,
    PowerRestored,
    LidForced,
    Slip,
    SlipFix,
    Unconscious,
    Critical,
    DefibMissing,
    DefibCharge,
    DefibSuccess,
    DefibFailure,
    Complete,
    QueueComplete,
    DiskMissing,
    DiskInserted,
    DiskRemoved,
    Idle,
    Emag,
    Offline,
}

/// <summary>A block of surgeries the pod can perform. The base library ships on the machine; the rest need a disk.</summary>
[Prototype("autodocProgram")]
public sealed partial class AutodocProgramPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>Surgery prototype ids this program unlocks.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Surgeries = new();
}

/// <summary>
/// Per-surgery autodoc overrides. The prototype id is the surgery's own id; a surgery with no entry uses the
/// pod's defaults.
/// </summary>
[Prototype("autodocProcedure")]
public sealed partial class AutodocProcedurePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Anaesthetic units before the first step. Zero means the procedure does not open the body.</summary>
    [DataField]
    public float? Anaesthetic;

    /// <summary>Antibiotic units after the last step.</summary>
    [DataField]
    public float? Antibiotic;
}

/// <summary>
/// The whole list of reagents the pod will draw out of a beaker. Nothing outside this list is administrable,
/// which keeps the rule in one Wolfmed-side file instead of a flag on every reagent prototype.
/// </summary>
[Prototype("autodocReagents")]
public sealed partial class AutodocReagentsPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<AutodocReagentEntry> Reagents = new();
}

[DataDefinition]
public sealed partial class AutodocReagentEntry
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    [DataField(required: true)]
    public AutodocReagentRole Role;

    /// <summary>False parks a reagent in the list without letting the pod use it.</summary>
    [DataField]
    public bool AutodocAdministrable = true;
}

public enum AutodocReagentRole : byte
{
    Anaesthetic,
    Antibiotic,
    Antiseptic,
    Fluid,
    Oxygen,
}
