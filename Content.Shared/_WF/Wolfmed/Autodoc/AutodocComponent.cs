using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Part;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>
/// A one-tile surgical pod ("S.A.M.") that performs real Shitmed surgery steps on its occupant with an
/// internal toolset. Every runtime field below is written on the server only; the component is networked so
/// the shared surgery system can see that an occupant is lying on an operating platform.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AutodocComponent : Component
{
    public const string BodyContainerId = "autodoc_body";
    public const string ToolContainerId = "autodoc_tools";
    public const string TraySlotId = "autodoc_tray";
    public const string DiskSlotId = "autodoc_disk";
    public const string ModuleSlotId = "autodoc_module";

    /// <summary>The three reservoir beaker slots, in UI order.</summary>
    public static readonly string[] ReservoirSlotIds = { "autodoc_beaker1", "autodoc_beaker2", "autodoc_beaker3" };

    /// <summary>Tool entities spawned into the tool container on map init. The pod never uses anything else.</summary>
    [DataField]
    public List<EntProtoId> Tools = new();

    [DataField]
    public ProtoId<AutodocVoicePrototype> Voice = "WolfmedAutodocVoiceSam";

    [DataField]
    public ProtoId<AutodocReagentsPrototype> Reagents = "WolfmedAutodocReagents";

    /// <summary>Programs the pod knows without any disk in the slot.</summary>
    [DataField]
    public List<ProtoId<AutodocProgramPrototype>> BasePrograms = new();

    /// <summary>Step time multiplier before machine parts. 1.25 means a quarter slower than a surgeon.</summary>
    [DataField]
    public float StepTime = 1.25f;

    /// <summary>Step time at the best manipulator tier, interpolated from <see cref="StepTime"/> by part rating.</summary>
    [DataField]
    public float BestStepTime = 0.9f;

    /// <summary>Malfunction chance per step before machine damage scales it.</summary>
    [DataField]
    public float MalfunctionChance = 0.02f;

    /// <summary>Wound the malfunction slip opens on the part being operated on.</summary>
    [DataField]
    public ProtoId<Content.Shared._Onyx.Wounds.WoundPrototype> SlipWound = "SlashWound";

    [DataField]
    public float SlipSeverity = 8f;

    /// <summary>Anaesthetic units drawn before the first incision of a procedure that opens the body.</summary>
    [DataField]
    public float DefaultAnaesthetic = 15f;

    /// <summary>Antibiotic units pushed after a procedure closes.</summary>
    [DataField]
    public float DefaultAntibiotic = 10f;

    /// <summary>Reservoir capacity the UI draws each beaker bar against, before matter bins.</summary>
    [DataField]
    public float BaseReservoirSize = 50f;

    [DataField]
    public ProtoId<MachinePartPrototype> MachinePartManipulator = "Manipulator";

    [DataField]
    public ProtoId<MachinePartPrototype> MachinePartMatterBin = "MatterBin";

    [DataField]
    public float IdleChatterMin = 90f;

    [DataField]
    public float IdleChatterMax = 180f;

    [DataField]
    public SoundSpecifier? LidOpenSound = new SoundPathSpecifier("/Audio/Machines/airlock_ext_open.ogg");

    [DataField]
    public SoundSpecifier? LidCloseSound = new SoundPathSpecifier("/Audio/Machines/airlock_ext_close.ogg");

    /// <summary>Damage on the pod itself past which it faults instead of operating.</summary>
    [DataField]
    public float FaultDamage = 60f;

    /// <summary>Volume of the voice lines, in dB.</summary>
    [DataField]
    public float VoiceGain = -2f;

    /// <summary>The transcript of the last line spoken, for the terminal window.</summary>
    [ViewVariables] public string? LastLine;

    /// <summary>Lines waiting behind the one being spoken. Anything past this is dropped.</summary>
    [DataField]
    public int VoiceQueueMax = 3;

    /// <summary>Seconds of silence idle chatter waits for before it will speak.</summary>
    [DataField]
    public float VoiceChatterSilence = 8f;

    /// <summary>Gain the step and tool sounds drop to while a voice line is playing.</summary>
    [DataField]
    public float DuckedToolGain = 0.6f;

    // Runtime state, written on the server only.

    [ViewVariables]
    public AutodocState State = AutodocState.Idle;

    /// <summary>Manipulator rating, 1 to 4, written by the machine part refresh.</summary>
    [ViewVariables]
    public float PartRating = 1f;

    /// <summary>Matter bin scaled capacity.</summary>
    [ViewVariables]
    public float ReservoirSize = 50f;

    /// <summary>The surgery that owns the step being performed, which may be a requirement of the queued one.</summary>
    [ViewVariables]
    public EntProtoId? CurrentSurgery;

    /// <summary>Paused because the power went out rather than because somebody asked.</summary>
    [ViewVariables]
    public bool PowerPaused;

    [ViewVariables]
    public bool PauseRequested;

    [ViewVariables]
    public bool AbortRequested;

    /// <summary>The "patient is asleep" line is said once per occupant, not once per tick.</summary>
    [ViewVariables]
    public bool SaidUnconscious;

    /// <summary>The queue of procedures, in execution order.</summary>
    [ViewVariables]
    public List<AutodocQueued> Queue = new();

    /// <summary>The lid refuses to open while this is set.</summary>
    [ViewVariables]
    public bool Locked;

    /// <summary>Set once the emagged pod has said its first line, which is when examine gives it away.</summary>
    [ViewVariables]
    public bool EmagRevealed;

    /// <summary>Operator-less mode: the occupant picked the procedure itself and gets no queue.</summary>
    [ViewVariables]
    public bool SelfService;

    /// <summary>Self-service anaesthesia toggle.</summary>
    [ViewVariables]
    public bool Anaesthesia = true;

    /// <summary>Whoever pressed Start, for the admin log. Null in self-service.</summary>
    [ViewVariables]
    public EntityUid? Operator;

    /// <summary>Seconds left in the current step.</summary>
    [ViewVariables]
    public float StepRemaining;

    /// <summary>Seconds the current step started with, so the UI can draw a bar.</summary>
    [ViewVariables]
    public float StepLength;

    /// <summary>The step prototype the pod is currently performing, for the UI and the voice.</summary>
    [ViewVariables]
    public EntProtoId? CurrentStep;

    /// <summary>What the pod is waiting for, or null.</summary>
    [ViewVariables]
    public AutodocRequirement? Pending;

    /// <summary>The anaesthetic dose for the running procedure has already been pushed.</summary>
    [ViewVariables]
    public bool AnaestheticGiven;

    /// <summary>The missing-defib line has been said for this occupant; it is not repeated every procedure.</summary>
    [ViewVariables] public bool DefibWarned;

    [ViewVariables]
    public TimeSpan NextIdleChatter;

    /// <summary>Lines waiting for the current one to finish, in speaking order.</summary>
    [ViewVariables]
    public List<AutodocVoiceRequest> VoiceQueue = new();

    /// <summary>When the line being spoken ends. Silence before that is what chatter waits for.</summary>
    [ViewVariables]
    public TimeSpan VoiceBusyUntil;

    /// <summary>The audio stream of the line being spoken, so an urgent line can cut it off.</summary>
    [ViewVariables]
    public EntityUid? VoiceStream;

    /// <summary>Lines actually spoken, as opposed to queued or dropped. Counts for the queue tests.</summary>
    [ViewVariables]
    public int VoiceSpoken;

    /// <summary>Step families already announced in the running procedure; each one speaks once.</summary>
    [ViewVariables]
    public HashSet<AutodocStepFamily> SpokenFamilies = new();

    /// <summary>Test seam: forces the next malfunction roll to succeed.</summary>
    [ViewVariables]
    public bool ForceMalfunction;
}

/// <summary>A line the pod has accepted but is not speaking yet, with the arguments it was raised with.</summary>
public sealed class AutodocVoiceRequest
{
    public AutodocVoiceRequest(string line, (string, object)[] args)
    {
        Line = line;
        Args = args;
    }

    public readonly string Line;

    public readonly (string, object)[] Args;
}

/// <summary>
/// Step lines are grouped so a procedure announces each kind of work once instead of once a step. Which
/// family a step belongs to is read off the voice event the step resolved to.
/// </summary>
public enum AutodocStepFamily : byte
{
    Incision,
    Bleeding,
    Bone,
    Closing,
    Part,
    Organ,
    Mechanical,
    Other,
}

/// <summary>One queued procedure and the requirements worked out when it was queued.</summary>
public sealed class AutodocQueued
{
    public EntProtoId Surgery;
    public TargetBodyPart Part;
    public List<AutodocRequirement> Requirements = new();

    /// <summary>Steps the pod has actually performed, so a surgery that stops being valid reads as finished.</summary>
    public int StepsDone;
}

/// <summary>Something the pod needs in the tray or the reservoir before a step can run.</summary>
public sealed class AutodocRequirement
{
    public AutodocRequirementKind Kind;

    /// <summary>Component name an item must carry to satisfy an organ or item requirement.</summary>
    public string? Component;

    /// <summary>Body part type and symmetry for a part requirement.</summary>
    public BodyPartType PartType;

    public BodyPartSymmetry? Symmetry;

    /// <summary>Reagent and units for a reagent requirement.</summary>
    public string? Reagent;

    public float Units;

    /// <summary>Already in the tray, or already pushed.</summary>
    public bool Satisfied;
}

public enum AutodocRequirementKind : byte
{
    Part,
    Organ,
    Item,
    Reagent,
}

[Serializable, NetSerializable]
public enum AutodocState : byte
{
    Idle,
    Preparing,
    Step,
    Waiting,
    Paused,
    Complete,
    Faulted,
}

[Serializable, NetSerializable]
public enum AutodocVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum AutodocVisualState : byte
{
    Open,
    Closed,
    Operating,
    Unpowered,
}

/// <summary>
/// Raised on a surgery performer that has no hands to ask it for its own toolset. Answered by the autodoc
/// with its internal tools plus whatever is in the delivery tray.
/// </summary>
[ByRefEvent]
public record struct WolfmedSurgeryToolsEvent(List<EntityUid>? Tools = null);
