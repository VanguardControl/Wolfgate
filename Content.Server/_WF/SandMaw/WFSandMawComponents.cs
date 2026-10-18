using Content.Shared._WF.Tether;
using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SandMaw;

/// <summary>
/// A maw in the sand. It lashes a tentacle round whoever comes near and reels them in, sucks in loose things, and
/// swallows what reaches its throat into a belly of flesh, made (with the loot of earlier meals) the first time it
/// eats. Slashing the tentacle frees its catch; breaking a belly wall tears a way back up.
/// </summary>
[RegisterComponent]
public sealed partial class WFSandMawComponent : Component
{
    /// <summary>Metres within which it lashes out.</summary>
    [DataField]
    public float GrabRange = 6f;

    [DataField]
    public TimeSpan GrabCooldown = TimeSpan.FromSeconds(4);

    /// <summary>Metres a second the tentacle reels in.</summary>
    [DataField]
    public float ReelSpeed = 1.2f;

    [DataField]
    public ProtoId<RopeTypePrototype> Tentacle = "WFSandMawTentacle";

    [DataField]
    public EntProtoId Grip = "WFSandMawTentacleGrip";

    /// <summary>Metres within which loose things slide in.</summary>
    [DataField]
    public float SuctionRadius = 3f;

    /// <summary>Pull on loose things at the throat, in m/s².</summary>
    [DataField]
    public float Pull = 4f;

    /// <summary>Metres from the centre at which it swallows.</summary>
    [DataField]
    public float SwallowRadius = 0.7f;

    /// <summary>Floor radius of the belly, in tiles.</summary>
    [DataField]
    public int BellyRadius = 4;

    [DataField]
    public EntProtoId BellyWall = "WFSandMawBellyWall";

    /// <summary>The belly's floor tile.</summary>
    [DataField]
    public string BellyFloor = "FloorFlesh";

    [DataField]
    public EntProtoId Acid = "WFSandMawAcid";

    [DataField]
    public EntProtoId Glow = "WFSandMawGlow";

    /// <summary>What earlier meals left in the belly.</summary>
    [DataField]
    public EntityTableSelector? Loot;

    [DataField]
    public SoundSpecifier? SwallowSound = new SoundPathSpecifier("/Audio/Effects/Fluids/slosh.ogg");

    [DataField]
    public SoundSpecifier? LashSound = new SoundPathSpecifier("/Audio/Effects/gib1.ogg");

    [ViewVariables]
    public EntityUid? Belly;

    [ViewVariables]
    public TimeSpan NextGrab;

    /// <summary>Who it has hold of, with the rope and the coil on them.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, WFSandMawGrab> Grabs = new();
}

public sealed class WFSandMawGrab
{
    public EntityUid Rope;
    public EntityUid Grip;

    /// <summary>Whether the victim got its rope point from the maw, to take away again.</summary>
    public bool AddedPoint;
}

/// <summary>The tentacle's coil round a victim; slash it apart to get free.</summary>
[RegisterComponent]
public sealed partial class WFSandMawGripComponent : Component
{
    [ViewVariables]
    public EntityUid Maw;

    [ViewVariables]
    public EntityUid Victim;
}

/// <summary>A wall of a maw's belly; breaking it opens a way back to the surface.</summary>
[RegisterComponent]
public sealed partial class WFSandMawBellyWallComponent : Component
{
    [ViewVariables]
    public EntityUid Maw;

    [DataField]
    public EntProtoId Exit = "WFSandMawExit";
}

/// <summary>A tear in a belly wall: whoever steps into it climbs out beside the maw.</summary>
[RegisterComponent]
public sealed partial class WFSandMawExitComponent : Component
{
    [ViewVariables]
    public EntityUid Maw;
}
