using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Audio;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WF.PlanetCracker;

/// <summary>
/// Plays the local listener's planet soundscape. Playback is deliberately global and client-only: at most two crossfading beds and
/// one environmental accent, with no per-tile sources or server audio entities.
/// </summary>
public sealed partial class WFPlanetAmbienceSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;

    private const float FadeRate = 12f;
    private const float AudibleMargin = 2f;

    private EntityUid? _loop;
    private EntityUid? _outgoingLoop;
    private string? _playlistKey;
    private int _loopIndex = -1;
    private float _crossfadeElapsed;
    private float _crossfadeDuration = 4f;
    private TimeSpan _nextLoop;
    private EntityUid? _oneShot;
    private string? _profileId;
    private string? _accentKey;
    private float _loopVolume = WFPlanetAmbience.SilentVolume;
    private float _oneShotVolume = WFPlanetAmbience.SilentVolume;
    private float _sliderVolume;
    private TimeSpan _nextOneShot;
    private bool _awaitingFirstAccent = true;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;

        Subs.CVar(_cfg, CCVars.AmbienceVolume, OnAmbienceVolumeChanged, true);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Shutdown()
    {
        StopAll();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        if (!TryGetContext(out var profile, out var layerOffset, out var night))
        {
            StopAll();
            return;
        }

        if (_profileId != profile.ID)
        {
            // Planet boundaries are a hard stop; crossfades belong within one world's playlist.
            StopAll();
            _profileId = profile.ID;
            _awaitingFirstAccent = true;
        }

        CullFinishedStreams();

        var hullOffset = IsAboardHull() ? profile.HullVolumeOffset : 0f;
        var commonOffset = layerOffset + hullOffset + _sliderVolume;
        var loopTarget = ClampVolume(profile.LoopVolume + commonOffset);
        var oneShotTarget = ClampVolume(profile.OneShotVolume + commonOffset);

        UpdateLoop(profile, night, loopTarget, frameTime);
        UpdateOneShot(profile, night, oneShotTarget, frameTime);
    }

    private bool TryGetContext(out WFPlanetAmbiencePrototype profile, out float layerOffset, out bool night)
    {
        profile = default!;
        night = false;
        layerOffset = WFPlanetAmbience.SilentVolume;

        if (_player.LocalEntity is not { } player ||
            !TryComp(player, out TransformComponent? xform) ||
            xform.MapUid is not { } map)
            return false;

        // A newly-created transit map may arrive one state before its soundscape snapshot.
        if (!HasComp<WFPlanetEnvironmentComponent>(map) &&
            TryComp<CEZTransitMapComponent>(map, out var transit) && transit.LowerMap is { } lower)
            map = lower;
        if (!TryComp<WFPlanetAmbienceComponent>(map, out var ambience) ||
            !TryComp<WFPlanetEnvironmentComponent>(map, out var environment) ||
            !_proto.TryIndex(ambience.Profile, out var resolved))
            return false;

        profile = resolved;
        night = environment.IsNight;
        layerOffset = ambience.VolumeOffset;
        return true;
    }

    private bool IsAboardHull()
    {
        if (_player.LocalEntity is not { } player || !TryComp(player, out TransformComponent? xform))
            return false;

        // Procedural ground uses its map as its grid. A detached planet chunk is also exposed terrain; every other
        // separate grid is a hull and gets the quieter indoor mix.
        return xform.GridUid is { } grid &&
               grid != xform.MapUid &&
               !HasComp<WFPlanetChunkComponent>(grid);
    }

    private void UpdateLoop(WFPlanetAmbiencePrototype profile, bool night, float target, float frameTime)
    {
        var playlist = profile.GetLoops(night);
        var phase = profile.DayLoops.Count == 0 && profile.NightLoops.Count == 0 ? "all" : night ? "night" : "day";
        var key = profile.ID + "/" + phase;
        var audible = target > WFPlanetAmbience.SilentVolume + AudibleMargin;
        var change = _playlistKey != key;
        // Finish an existing blend before starting another; never allocate a third bed.
        if (audible && playlist.Count > 0 && _outgoingLoop == null &&
            (_loop == null || change || playlist.Count > 1 && _timing.CurTime >= _nextLoop))
        {
            var index = change ? 0 : (_loopIndex + 1) % playlist.Count;
            var stream = _audio.PlayGlobal(playlist[index], Filter.Local(), false,
                AudioParams.Default.WithLoop(true).WithVolume(WFPlanetAmbience.SilentVolume));
            if (stream != null)
            {
                _outgoingLoop = _loop;
                _loop = stream.Value.Entity;
                _playlistKey = key;
                _loopIndex = index;
                Log.Debug($"Planet ambience: {key}, {stream.Value.Component.FileName}");
                _crossfadeElapsed = 0;
                var length = (float) _audio.GetAudioLength(new ResolvedPathSpecifier(stream.Value.Component.FileName)).TotalSeconds;
                if (!float.IsFinite(length) || length <= 0)
                    length = 60f;
                _crossfadeDuration = Math.Clamp(profile.CrossfadeSeconds, 0.1f, MathF.Max(0.1f, length / 4));
                _nextLoop = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(_crossfadeDuration + 1, length - _crossfadeDuration));
            }
        }

        _loopVolume = Approach(_loopVolume, target, FadeRate * frameTime);
        _crossfadeElapsed = MathF.Min(_crossfadeElapsed + frameTime, _crossfadeDuration);
        var blend = Math.Clamp(_crossfadeElapsed / _crossfadeDuration, 0f, 1f);
        var gain = SharedAudioSystem.VolumeToGain(_loopVolume);
        // Equal-power overlap avoids an audible dip between different recordings.
        _audio.SetGain(_loop, gain * MathF.Max(0f, MathF.Sin(blend * MathF.PI / 2)));
        _audio.SetGain(_outgoingLoop, gain * MathF.Max(0f, MathF.Cos(blend * MathF.PI / 2)));
        if (blend >= 1)
            _outgoingLoop = StopStream(_outgoingLoop);
        if (target <= WFPlanetAmbience.SilentVolume && _loopVolume <= WFPlanetAmbience.SilentVolume)
        {
            _loop = StopStream(_loop);
            _outgoingLoop = StopStream(_outgoingLoop);
        }
    }

    private void UpdateOneShot(WFPlanetAmbiencePrototype profile, bool night, float target, float frameTime)
    {
        var sounds = profile.GetOneShots(night);
        var phase = profile.DayOneShots.Count == 0 && profile.NightOneShots.Count == 0 ? "all" : night ? "night" : "day";
        var key = profile.ID + "/" + phase;
        if (_accentKey != key)
        {
            _oneShot = StopStream(_oneShot);
            _accentKey = key;
            _awaitingFirstAccent = true;
        }
        if (target <= WFPlanetAmbience.SilentVolume + AudibleMargin)
            _awaitingFirstAccent = true;
        else if (_awaitingFirstAccent)
        {
            // Start the arrival cue after reaching an audible layer, not during silent orbit.
            _nextOneShot = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(6f, 12f));
            _awaitingFirstAccent = false;
        }

        if (_oneShot != null)
        {
            _oneShotVolume = Approach(_oneShotVolume, target, FadeRate * frameTime);
            _audio.SetVolume(_oneShot, _oneShotVolume);

            if (target <= WFPlanetAmbience.SilentVolume && _oneShotVolume <= WFPlanetAmbience.SilentVolume)
                _oneShot = StopStream(_oneShot);

            return;
        }

        if (_timing.CurTime < _nextOneShot)
            return;

        if (target <= WFPlanetAmbience.SilentVolume + AudibleMargin || sounds.Count == 0)
        {
            ScheduleOneShot(profile);
            return;
        }

        _oneShotVolume = target;
        var sound = _random.Pick(sounds);
        _oneShot = _audio.PlayGlobal(
            sound,
            Filter.Local(),
            false,
            AudioParams.Default
                .WithVariation(0.04f)
                .WithVolume(_oneShotVolume))?.Entity;
        Log.Debug($"Planet accent: {profile.ID}, {sound}");
        ScheduleOneShot(profile);
    }

    private void CullFinishedStreams()
    {
        if (_loop != null && !TryComp<AudioComponent>(_loop, out _))
            _loop = null;

        if (_outgoingLoop != null && !TryComp<AudioComponent>(_outgoingLoop, out _))
            _outgoingLoop = null;

        if (_oneShot != null && !TryComp<AudioComponent>(_oneShot, out _))
            _oneShot = null;
    }

    private void ScheduleOneShot(WFPlanetAmbiencePrototype profile)
    {
        var maximum = MathF.Max(profile.MinInterval, profile.MaxInterval);
        var minimum = MathF.Min(profile.MinInterval, maximum);
        var delay = minimum < maximum ? _random.NextFloat(minimum, maximum) : minimum;
        _nextOneShot = _timing.CurTime + TimeSpan.FromSeconds(delay);
    }

    private void OnAmbienceVolumeChanged(float gain)
    {
        var volume = SharedAudioSystem.GainToVolume(gain);
        _sliderVolume = float.IsFinite(volume) ? MathF.Max(volume, WFPlanetAmbience.SilentVolume) : WFPlanetAmbience.SilentVolume;
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        StopAll();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        StopAll();
    }

    private EntityUid? StopStream(EntityUid? stream)
    {
        if (stream is { } uid && TryComp<AudioComponent>(uid, out var audio))
        {
            // These streams are client-owned. SharedAudioSystem.Stop refuses to stop during
            // state application, exactly when local-player detach/ghost changes can arrive.
            // Do not lose the handle while leaving an untracked global loop behind.
            _audio.SetGain(uid, 0f, audio);
            QueueDel(uid);
        }
        return null;
    }

    private void StopAll()
    {
        _loop = StopStream(_loop);
        _outgoingLoop = StopStream(_outgoingLoop);
        _playlistKey = null;
        _loopIndex = -1;
        _crossfadeElapsed = 0;
        _nextLoop = TimeSpan.Zero;
        _oneShot = StopStream(_oneShot);
        _profileId = null;
        _accentKey = null;
        _loopVolume = WFPlanetAmbience.SilentVolume;
        _oneShotVolume = WFPlanetAmbience.SilentVolume;
        _nextOneShot = TimeSpan.Zero;
        _awaitingFirstAccent = true;
    }

    private static float ClampVolume(float value)
        => Math.Clamp(value, WFPlanetAmbience.SilentVolume, 0f);

    private static float Approach(float current, float target, float amount)
    {
        if (current < target)
            return MathF.Min(current + amount, target);

        return MathF.Max(current - amount, target);
    }
}
