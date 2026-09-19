using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Utility;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>Marks a grid as a planet cracker hull and carries the state of its current crack.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class WFPlanetCrackerComponent : Component
{
    /// <summary>Current stage of the crack, per design section 3.</summary>
    [DataField, AutoNetworkedField]
    public WFCrackState State = WFCrackState.Idle;

    /// <summary>
    /// Grid file holding the anchor transport that ships with this hull; loaded and docked when the vessel is bought.
    /// Null on the code-built test hull, which has no map file to load and builds its transport in code instead.
    /// </summary>
    [DataField]
    public ResPath? TransportMap;

    /// <summary>True once this hull's transport has been spawned, so a repeated purchase event cannot duplicate it.</summary>
    [ViewVariables]
    public bool TransportSpawned;

    /// <summary>The mapper-placed berth marker on this hull, resolved at map init.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Berth;

    /// <summary>The first half of the targeted pair.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? AnchorA;

    /// <summary>The second half of the targeted pair.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? AnchorB;

    /// <summary>
    /// Berth centre in grid-local coordinates, rewritten whenever the berth resolves.
    /// The grid entity is force-sent to any client who sees any chunk of it, while the berth marker 26 tiles out on a
    /// MarkerBase prototype routinely falls outside net.pvs_range, so this is how the berth pose reaches a client that
    /// has no BUI state to read - the radar ghost.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 BerthLocalPos;

    /// <summary>Berth rotation relative to the grid, rewritten alongside <see cref="BerthLocalPos"/>.</summary>
    [DataField, AutoNetworkedField]
    public Angle BerthLocalRot;

    /// <summary>Berth rectangle in tiles, copied off the marker.</summary>
    [DataField, AutoNetworkedField]
    public Vector2i BerthSize;

    /// <summary>
    /// When the running crack finishes; authoritative while the crack is not damage-paused.
    /// Two fields rather than one deadline: the map pause and the damage pause are different mechanisms and coexist,
    /// so the remainder is banked in <see cref="CrackRemaining"/> while a damaged anchor holds the cut.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan CrackEnd;

    /// <summary>Time left on the crack, banked while the damage pause holds.</summary>
    [DataField]
    public TimeSpan CrackRemaining;

    /// <summary>Full duration the current crack was begun with, for the console progress bar.</summary>
    [DataField]
    public TimeSpan CrackDuration;

    /// <summary>True while a damaged targeted anchor is holding the crack.</summary>
    [DataField]
    public bool CrackPaused;

    /// <summary>When the grace countdown runs out and the hull drops; meaningless unless GraceRunning.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan GraceEnd;

    /// <summary>True while the grace countdown is running.</summary>
    [DataField]
    public bool GraceRunning;

    /// <summary>How long the crew has to fix a failing precondition before the hull drops.</summary>
    [DataField]
    public TimeSpan GraceDuration = TimeSpan.FromMinutes(5);

    /// <summary>Which preconditions are failing; recomputed every sweep while Cracking or Cracked.</summary>
    [DataField]
    public WFCrackFailure Failing;

    /// <summary>Why BEGIN CRACK is refused right now; the console hover list names one locale key per flag.</summary>
    [DataField]
    public WFCrackBlocker Blockers;

    /// <summary>When the abort spin-down finishes and the state falls back to PendingAbort.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan AbortEnd;

    /// <summary>State the abort spin-down is heading for; null when no abort is running.</summary>
    [DataField, AutoNetworkedField]
    public WFCrackState? PendingAbort;

    /// <summary>True only while the crack applied its own ForceAnchor, so the release never steals a mapper's anchor.</summary>
    [DataField]
    public bool Locked;

    /// <summary>Tiles the berth centre may sit from the cut circle centre and still be targetable.</summary>
    [DataField]
    public float AlignTolerance = 8f;

    /// <summary>Crack time at ReferenceDistance with tier 1 parts.</summary>
    [DataField]
    public TimeSpan BaseCrackTime = TimeSpan.FromMinutes(12);

    /// <summary>Pair distance BaseCrackTime is quoted for, in tiles.</summary>
    [DataField]
    public float ReferenceDistance = 24f;

    /// <summary>How long the hull keeps its lock after a targeted anchor is broken or destroyed.</summary>
    [DataField]
    public TimeSpan AbortSpinDown = TimeSpan.FromSeconds(30);

    /// <summary>Healthy projectors the hull needs to keep cutting.</summary>
    [DataField]
    public int RequiredProjectors = 2;

    /// <summary>The chunk grid hanging in this hull's berth, once one has been cut.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Chunk;

    /// <summary>The anchor that was switched off first, holding the disconnect pairing window open.</summary>
    [DataField]
    public NetEntity? DisconnectAnchor;

    /// <summary>When the disconnect pairing window lapses and the first anchor re-arms; meaningless unless DisconnectArmed.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan DisconnectEnd;

    /// <summary>True while the disconnect pairing window is open and waiting on the second anchor.</summary>
    [DataField]
    public bool DisconnectArmed;

    /// <summary>How many disconnect-window popup beats have already fired, so a re-entered sweep cannot repeat one.</summary>
    [DataField]
    public byte DisconnectBeat;

    /// <summary>How long the crew has to switch the second anchor off before the first re-arms.</summary>
    [DataField]
    public TimeSpan DisconnectWindow = TimeSpan.FromSeconds(60);

    /// <summary>When the evacuation runs out and the chunk is released; meaningless unless EvacRunning.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EvacEnd;

    /// <summary>True while the evacuation countdown is running.</summary>
    [DataField]
    public bool EvacRunning;

    /// <summary>How many evacuation popup beats have already fired, so a re-entered sweep cannot repeat one.</summary>
    [DataField]
    public byte EvacBeat;

    /// <summary>How long the crew has to clear the chunk once the disconnect is committed.</summary>
    [DataField]
    public TimeSpan EvacDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How often the evacuation alarm loop is stopped and replayed. A filtered PlayGlobal freezes its recipient set at
    /// play time, so without the re-issue a latecomer boarding mid-countdown would hear nothing.
    /// </summary>
    [DataField]
    public TimeSpan EvacReissue = TimeSpan.FromSeconds(15);

    /// <summary>When the evacuation alarm loop is next re-issued.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EvacNextLoop;

    /// <summary>When the post-release settle finishes and the hull leaves Released.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ReleaseEnd;

    /// <summary>How long the hull sits in Released after the chunk is away.</summary>
    [DataField]
    public TimeSpan ReleaseSettle = TimeSpan.FromSeconds(10);

    /// <summary>How often the site camera kick repeats while the cut runs, in seconds.</summary>
    [DataField]
    public float SiteKickInterval = 2f;

    /// <summary>Tiles added to the cut radius to get the range of the site camera kick.</summary>
    [DataField]
    public float SiteKickPadding = 6f;

    /// <summary>Looped while the cut runs.</summary>
    [DataField]
    public SoundSpecifier RumbleSound = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/ship_side_crack_ambience_loop.ogg");

    /// <summary>Looped while the grace countdown runs.</summary>
    [DataField]
    public SoundSpecifier KlaxonSound = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/gravity_gen_warning_alarm.ogg");

    /// <summary>Looped once the hull is falling.</summary>
    [DataField]
    public SoundSpecifier FallSound = new SoundPathSpecifier("/Audio/Misc/redalert.ogg");

    /// <summary>One-shot thunk as the hull snaps onto the circle and locks.</summary>
    [DataField]
    public SoundSpecifier LockSound = new SoundCollectionSpecifier("MetalThud");

    /// <summary>One-shot boom as the disc tears free; played globally on both layers at extraction.</summary>
    [DataField]
    public SoundSpecifier ExtractSound = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/crack_complete_1.ogg");

    /// <summary>The second completion sound, played everywhere the first is.</summary>
    [DataField]
    public SoundSpecifier ExtractSound2 = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/crack_complete_2.ogg");

    /// <summary>Looped on the hull while the evacuation countdown runs.</summary>
    [DataField]
    public SoundSpecifier EvacSound = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/chunk_release_alarm_loop.ogg");

    /// <summary>
    /// Looped on the ground layer at the cut circle while the cut runs. The hull's own rumble is replicated to orbit
    /// but the client zeroes gain across maps, so the site needs a source of its own or it is silent.
    /// </summary>
    [DataField]
    public SoundSpecifier GroundRumbleSound = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/planet_side_crack_ambience_loop_1.ogg");

    /// <summary>The second planet-side ambience loop, played alongside the first to the same audience.</summary>
    [DataField]
    public SoundSpecifier GroundAmbienceSound2 = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/planet_side_crack_ambience_loop_2.ogg");

    /// <summary>The beam igniting, once at every projector and anchor as the cut begins.</summary>
    [DataField]
    public SoundSpecifier BeamFireSound = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/beam_fire.ogg");

    /// <summary>The beam holding, looped at every projector and anchor for the whole cut.</summary>
    [DataField]
    public SoundSpecifier BeamLoopSound = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/beam_loop.ogg");

    /// <summary>One of the ground cracking open, played at the cut circle every hull rumble.</summary>
    [DataField]
    public SoundSpecifier CrackEffectSound = new SoundCollectionSpecifier("WFRandomCrackEffect");

    /// <summary>Live rumble loop; server-only, never networked, stopped on every state edge.</summary>
    [ViewVariables]
    public EntityUid? RumbleStream;

    /// <summary>Live grace klaxon loop; server-only, never networked.</summary>
    [ViewVariables]
    public EntityUid? KlaxonStream;

    /// <summary>Live fall alarm loop; server-only, never networked.</summary>
    [ViewVariables]
    public EntityUid? FallStream;

    /// <summary>Live ground-side rumble loop at the cut site; server-only, never networked.</summary>
    [ViewVariables]
    public EntityUid? GroundRumbleStream;

    /// <summary>The second planet-side ambience stream.</summary>
    [ViewVariables]
    public EntityUid? GroundAmbienceStream2;

    /// <summary>The beam loops, one per projector and per anchor, for the length of the cut.</summary>
    [ViewVariables]
    public List<EntityUid> BeamStreams = new();

    /// <summary>Live evacuation alarm loop; server-only, never networked.</summary>
    [ViewVariables]
    public EntityUid? EvacStream;
}
