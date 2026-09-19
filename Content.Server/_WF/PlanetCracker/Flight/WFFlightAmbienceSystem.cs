using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Flight;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Content.Server._WF.PlanetCracker.Planets;

namespace Content.Server._WF.PlanetCracker.Flight;

/// <summary>
/// The noise of being inside a planet's atmosphere. Every hull below orbit and off the ground carries a looping wind
/// stream whose volume and pitch follow its planar speed, and a falling one carries a second stream of airframe rumble
/// that rises as the fall approaches the speed a free fall lands at. Both are grid-filtered, so only the people aboard
/// hear them, and both are stopped by the component's own shutdown - landing, orbit and deletion all go through it.
/// </summary>
public sealed partial class WFFlightAmbienceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WFGridAudienceSystem _audience = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;

    /// <summary>Volume (dB) of the wind on a hull that is barely moving.</summary>
    public const float WindMinVolume = -9f;

    /// <summary>Volume (dB) of the wind at <see cref="WindMaxSpeed"/> and above.</summary>
    public const float WindMaxVolume = 1f;

    /// <summary>Planar speed (m/s) at which the wind is as loud and as high as it gets.</summary>
    public const float WindMaxSpeed = 14f;

    /// <summary>Volume (dB) of the airframe rumble at the speed a free fall lands at.</summary>
    public const float RumbleMaxVolume = -3f;

    /// <summary>Volume (dB) the rumble fades in from, at the moment the lift goes.</summary>
    private const float RumbleMinVolume = -20f;

    private const float WindMinPitch = 0.8f;
    private const float WindMaxPitch = 1.25f;

    /// <summary>Pitch step the wind is quantised to, so a hull under thrust is not re-cutting its loop every sweep.</summary>
    private const float WindPitchStep = 0.05f;

    /// <summary>How often every grid's flight state is looked at; this is ambience, not physics.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    /// <summary>How often both loops are re-cut for latecomers, exactly as the evacuation alarm re-issues its own.</summary>
    private static readonly TimeSpan ReissueInterval = TimeSpan.FromSeconds(30);

    /// <summary>The airstream itself, the loud part of flying with an atmosphere outside.</summary>
    public static readonly SoundSpecifier WindSound =
        new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Flight/atmo_wind.ogg");

    /// <summary>The hull's own structure complaining, under the wind and only while the hull is coming down.</summary>
    public static readonly SoundSpecifier RumbleSound =
        new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Flight/fall_rumble.ogg");

    private readonly List<EntityUid> _scan = new();

    private TimeSpan _nextSweep;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // The one directed subscription on this component; nothing else subscribes the pair.
        SubscribeLocalEvent<WFFlightAmbienceComponent, ComponentShutdown>(OnShutdown);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;

        _scan.Clear();

        var grids = EntityQueryEnumerator<MapGridComponent>();
        while (grids.MoveNext(out var uid, out _))
        {
            // A z-layer map is itself a grid; it is never a hull in flight, and must not sing to everyone on the layer.
            if (HasComp<MapComponent>(uid))
                continue;

            _scan.Add(uid);
        }

        foreach (var grid in _scan)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            if (InAtmosphere(grid))
                UpdateAmbience(grid);
            else
                StopAmbience(grid);
        }
    }

    /// <summary>
    /// True for a grid that is actually flying through a planet's air: any layer or gap below the orbit layer, down to
    /// but not including the ground it lands on. Orbit is vacuum and the ground is not flight.
    /// </summary>
    public bool InAtmosphere(EntityUid grid)
    {
        if (Transform(grid).MapUid is not { } map)
            return false;

        if (HasComp<WFOrbitLayerComponent>(map))
            return false;

        // A gap is always flight, whichever two layers it hangs between.
        if (!HasComp<CEZTransitMapComponent>(map) && IsGroundLayer(map))
            return false;

        return _zLevels.WfIsPlanetFlight(grid);
    }

    /// <summary>The bottom of a planet stack, which is where flying stops.</summary>
    private bool IsGroundLayer(EntityUid map)
    {
        return HasComp<CEZGroundLayerComponent>(map)
               || (TryComp<CEZMapComponent>(map, out var zMap) && zMap.Depth == 0);
    }

    /// <summary>Brings one hull's two loops in line with how fast it is going and how hard it is falling.</summary>
    private void UpdateAmbience(EntityUid grid)
    {
        var comp = EnsureComp<WFFlightAmbienceComponent>(grid);

        var reissue = _timing.CurTime >= comp.NextReissue;

        if (reissue)
            comp.NextReissue = _timing.CurTime + ReissueInterval;

        var speed = TryComp<PhysicsComponent>(grid, out var body) ? body.LinearVelocity.Length() : 0f;
        var wind = Math.Clamp(speed / WindMaxSpeed, 0f, 1f);
        var pitch = MathF.Round((WindMinPitch + (WindMaxPitch - WindMinPitch) * wind) / WindPitchStep) * WindPitchStep;
        var volume = WindMinVolume + (WindMaxVolume - WindMinVolume) * wind;

        if (reissue || !Alive(comp.Wind) || !MathHelper.CloseTo(pitch, comp.WindPitch, 0.001f))
        {
            comp.WindPitch = pitch;
            comp.Wind = Replay(comp.Wind, WindSound, grid, volume, pitch);
        }
        else
        {
            _audio.SetVolume(comp.Wind, volume);
        }

        if (!HasComp<WFLiftLostComponent>(grid))
        {
            comp.Rumble = _audio.Stop(comp.Rumble);
            return;
        }

        var fall = 0f;

        if (TryComp<CEZGridFallerComponent>(grid, out var faller))
        {
            var reference = _zLevels.WfGetFreeFallSpeed(faller);

            if (reference > 0f)
                fall = Math.Clamp(faller.Velocity / reference, 0f, 1f);
        }

        var rumble = RumbleMinVolume + (RumbleMaxVolume - RumbleMinVolume) * fall;

        if (reissue || !Alive(comp.Rumble))
            comp.Rumble = Replay(comp.Rumble, RumbleSound, grid, rumble, 1f);
        else
            _audio.SetVolume(comp.Rumble, rumble);
    }

    /// <summary>Cuts both loops and lets the grid be an ordinary grid again; the shutdown handler does the stopping.</summary>
    public void StopAmbience(EntityUid grid)
    {
        if (HasComp<WFFlightAmbienceComponent>(grid))
            RemComp<WFFlightAmbienceComponent>(grid);
    }

    /// <summary>The only place a stream dies: landing, orbit and the grid going away all reach it.</summary>
    private void OnShutdown(Entity<WFFlightAmbienceComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Wind = _audio.Stop(ent.Comp.Wind);
        ent.Comp.Rumble = _audio.Stop(ent.Comp.Rumble);
    }

    /// <summary>
    /// Stops whatever was playing and starts the same loop again for everyone aboard now. PlayGlobal to a grid filter
    /// rather than PlayPvs on the grid: that parents the audio at the grid's local origin with a 15 tile default
    /// range, which on a capital hull is wind in one corridor (WFCrackerSystem.Disconnect.cs).
    /// </summary>
    private EntityUid? Replay(EntityUid? stream, SoundSpecifier sound, EntityUid grid, float volume, float pitch)
    {
        _audio.Stop(stream);

        return _audio.PlayGlobal(
            sound,
            _audience.Aboard(grid),
            true,
            AudioParams.Default.WithLoop(true).WithVolume(volume).WithPitchScale(pitch))?.Entity;
    }

    /// <summary>True when a remembered stream is still a thing that can be adjusted rather than replaced.</summary>
    private bool Alive(EntityUid? stream)
    {
        return stream is { } uid && !TerminatingOrDeleted(uid);
    }
}
