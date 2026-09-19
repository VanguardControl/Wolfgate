using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.PlanetCracker.Chunk;

/// <summary>Marks a grid as a disc cut out of a planet and hung in its cracker's berth (design D24).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class WFPlanetChunkComponent : Component
{
    /// <summary>The cracker hull this chunk was cut for; the watchdog drops the chunk once this stops resolving.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Cracker;

    /// <summary>The ground layer map the disc was lifted out of.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? GroundMap;

    /// <summary>
    /// The berth's map, captured at extraction so the watchdog compares map IDENTITY rather than kind: the orbit layer
    /// is an FTL destination and every planet has one, so a hull that jumps to another planet's orbit must still drop.
    /// </summary>
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

    /// <summary>
    /// True only when the drop's TryEnterTransit actually took: <see cref="Dropped"/> is set either way, and the
    /// landing test is "my map is no longer a transit map", which a chunk still sitting in its berth passes on the very
    /// first sweep. Without this a failed push would be read as a landing and the grid deleted after CleanupDelay.
    /// </summary>
    [DataField]
    public bool EnteredTransit;

    /// <summary>
    /// When the chunk was cut. Paused with its map, because a plain TimeSpan would burn the whole watchdog grace the
    /// instant a paused map unpaused and drop the chunk out from under a perfectly healthy hull.
    /// </summary>
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

    /// <summary>
    /// How long the wreck is left alone after landing before it is cleaned up. It must outlast the crash explosion
    /// drain: those blasts are anchored to coordinates parented to this grid, so deleting it early voids them.
    /// </summary>
    [DataField]
    public TimeSpan CleanupDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// World pose snapshotted at drop time and re-asserted once at landing; the chunk is a dynamic body for the whole
    /// fall and nothing re-disables it, so ground friction and wall collision would otherwise slide it off the crater.
    /// </summary>
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

    /// <summary>
    /// Per-tile crash blast intensity, copied onto CEZGridFallerComponent at drop time. The engine's own central blast
    /// is suppressed there by writing CrashIntensityPerTile = 0, because it is centred on the grid origin - which for a
    /// chunk is the GROUND grid's origin, hundreds of tiles from the cut circle on a real biome planet. No replacement
    /// central blast is queued: ExplosionSystem.QueueExplosion merges same-prototype explosions within one tile by
    /// adding intensity only, so a second blast at the crater is arithmetically identical to raising this field.
    /// </summary>
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
