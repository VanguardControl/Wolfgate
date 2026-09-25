using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.PlanetCracker.Chunk;

/// <summary>Marks a grid as a disc cut out of a planet and hung in its cracker's berth.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class WFPlanetChunkComponent : Component
{
    /// <summary>The cracker hull this chunk was cut for; the watchdog drops the chunk once this stops resolving.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Cracker;

    /// <summary>The ground layer map the disc was lifted out of.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? GroundMap;

    /// <summary>The berth's map at extraction; the watchdog compares identity so a jump to another planet's orbit still drops.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? OrbitMap;

    /// <summary>Centre of the cut circle as a raw world XY on the ground layer.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 HoleCentre;

    /// <summary>Radius of the cut circle in tiles.</summary>
    [DataField, AutoNetworkedField]
    public float Radius;

    /// <summary>How many tiles were copied onto this grid.</summary>
    [DataField, AutoNetworkedField]
    public int TileCount;

    /// <summary>True once the chunk has been pushed into transit; the watchdog skips it from then on.</summary>
    [DataField, AutoNetworkedField]
    public bool Dropped;

    /// <summary>True only when the drop's TryEnterTransit took, so a failed push is not read as a landing.</summary>
    [DataField]
    public bool EnteredTransit;

    /// <summary>When the chunk was cut; paused with its map so an unpause does not burn the watchdog grace.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ExtractedAt;

    /// <summary>How long after extraction the watchdog leaves the chunk alone.</summary>
    [DataField]
    public TimeSpan WatchdogGrace = TimeSpan.FromSeconds(5);

    /// <summary>True once the dropped chunk has left transit and settled on the ground layer.</summary>
    [DataField]
    public bool Landed;

    /// <summary>When the chunk settled; paused with its map, same as <see cref="ExtractedAt"/>.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LandedAt;

    /// <summary>How long a landed wreck is kept; must outlast the crash explosions, which are parented to this grid.</summary>
    [DataField]
    public TimeSpan CleanupDelay = TimeSpan.FromSeconds(10);

    /// <summary>World position snapshotted at drop and re-asserted at landing, so the dynamic body does not slide off the crater.</summary>
    [DataField]
    public Vector2 DropWorldPos;

    /// <summary>World rotation snapshotted alongside <see cref="DropWorldPos"/>.</summary>
    [DataField]
    public Angle DropWorldRot;

    /// <summary>Hull tiles the gangway to this chunk was laid on, lifted again at drop.</summary>
    [DataField]
    public List<Vector2i> GangwayTiles = new();

    /// <summary>The hull carrying the gangway.</summary>
    [DataField]
    public NetEntity? GangwayHull;

    /// <summary>Per-tile crash intensity copied onto CEZGridFallerComponent at drop; its central blast is zeroed (it would hit the ground origin).</summary>
    [DataField]
    public float CrashTileIntensity = 4f;

    /// <summary>Per-tile crash intensity cap, copied onto CEZGridFallerComponent at drop time beside CrashTileIntensity.</summary>
    [DataField]
    public float CrashTileMaxIntensity = 2f;

    /// <summary>True while the chunk's own evacuation alarm is running.</summary>
    [DataField]
    public bool Evacuating;

    /// <summary>When the evacuation alarm loop is next re-issued; paused with the map.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EvacNextLoop;

    /// <summary>Looped once the chunk is falling.</summary>
    [DataField]
    public SoundSpecifier DropSound = new SoundPathSpecifier("/Audio/Ambience/Objects/crushing.ogg");

    /// <summary>Looped on the chunk while the evacuation alarm runs.</summary>
    [DataField]
    public SoundSpecifier EvacSound = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/chunk_release_alarm_loop.ogg");

    /// <summary>Ids of the rim decals stamped around the hole, kept for admin teardown; there is no bulk decal removal.</summary>
    [ViewVariables]
    public List<uint> RimDecals = new();

    /// <summary>Live drop loop; server-only, never networked.</summary>
    [ViewVariables]
    public EntityUid? DropStream;

    /// <summary>Live evacuation alarm loop; server-only, never networked.</summary>
    [ViewVariables]
    public EntityUid? EvacStream;
}
