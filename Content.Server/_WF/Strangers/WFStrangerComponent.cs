using Content.Shared._WF.Strangers;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Strangers;

/// <summary>How a stranger goes about the people he sees.</summary>
public enum WFStrangerBehaviour : byte
{
    /// <summary>Walks up to whoever he sees and wants a word.</summary>
    Approach,

    /// <summary>Keeps his distance and follows, aboard their ship too. Never starts a conversation.</summary>
    Stalk,
}

/// <summary>A wandering oddball: someone who walks up to players and talks at them, or just follows them about.</summary>
[RegisterComponent, Access(typeof(WFStrangerSystem))]
public sealed partial class WFStrangerComponent : Component
{
    [DataField(required: true)]
    public ProtoId<WFStrangerDialoguePrototype> Dialogue;

    [DataField]
    public WFStrangerBehaviour Behaviour = WFStrangerBehaviour.Approach;

    /// <summary>How far off he notices someone.</summary>
    [DataField]
    public float SightRange = 14f;

    /// <summary>How close he comes before he hails.</summary>
    [DataField]
    public float TalkRange = 1.8f;

    /// <summary>How close a stalker lets himself get, and how far he lets them pull ahead.</summary>
    [DataField]
    public float StalkNear = 5f;

    [DataField]
    public float StalkFar = 9f;

    /// <summary>How long a stalker waits once his mark is aboard a ship before he turns up aboard it too.</summary>
    [DataField]
    public TimeSpan BoardDelay = TimeSpan.FromSeconds(25);

    /// <summary>Things a stalker mutters now and then.</summary>
    [DataField]
    public List<LocId> Mutters = new();

    [DataField]
    public TimeSpan MutterInterval = TimeSpan.FromSeconds(45);

    /// <summary>Who he's after or talking to.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>The node he's on with <see cref="Target"/>; null until they talk.</summary>
    [ViewVariables]
    public string? Node;

    /// <summary>Whether he has hailed the target and is waiting on an answer.</summary>
    [ViewVariables]
    public bool Hailed;

    /// <summary>People he's done with, and until when he leaves them be.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Done = new();

    /// <summary>When a stalker's mark boarded the ship he isn't on.</summary>
    [ViewVariables]
    public TimeSpan? MarkAboardSince;

    [ViewVariables]
    public TimeSpan NextMutter;

    [ViewVariables]
    public TimeSpan HailedAt;
}
