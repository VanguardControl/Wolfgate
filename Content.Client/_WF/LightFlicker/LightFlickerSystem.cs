using Content.Client.Light.Visualizers;
using Content.Shared._WF.LightFlicker;
using Content.Shared.Light;
using Content.Shared.Light.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WF.LightFlicker;

/// <summary>
/// Flickers powered lights locally: a short stutter when a tube strikes on, and a lasting fault while the ballast is
/// damaged. Nothing here is networked, so every client sees its own pattern.
/// </summary>
public sealed class LightFlickerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPointLightSystem _pointLight = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    /// <summary>How old the turn-on sound may be for a light to count as just switched on rather than just seen.</summary>
    private static readonly TimeSpan StrikeWindow = TimeSpan.FromSeconds(1);

    /// <summary>Volume of the starter tick a faulty ballast makes, relative to the light's turn-on sound.</summary>
    private const float FaultSoundVolume = -12f;

    /// <summary>How far the starter tick of a faulty ballast carries.</summary>
    private const float FaultSoundRange = 8f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PoweredLightComponent, AppearanceChangeEvent>(OnAppearanceChange,
            after: new[] { typeof(PoweredLightVisualizerSystem) });
        SubscribeLocalEvent<DamagedBallastComponent, ComponentStartup>(OnBallastStartup);
    }

    private void OnAppearanceChange(Entity<PoweredLightComponent> ent, ref AppearanceChangeEvent args)
    {
        if (!_appearance.TryGetData<PoweredLightState>(ent, PoweredLightVisuals.BulbState, out var state, args.Component))
            return;

        var flicker = EnsureComp<LightFlickerComponent>(ent);
        var wasOn = flicker.LastState == PoweredLightState.On;
        flicker.LastState = state;

        if (state != PoweredLightState.On || IsBlinking(ent))
        {
            Stop(ent, flicker, state == PoweredLightState.On);
            return;
        }

        if (HasComp<DamagedBallastComponent>(ent))
            StartFault(ent, flicker);
        // Only a light the server just switched on, not one that was already lit when it came into view.
        else if (!wasOn && _timing.CurTime - ent.Comp.LastThunk < StrikeWindow)
            StartStrike(ent, flicker);

        // Upstream's visualizer has just redrawn the light as lit, so put the flicker's state back.
        if (flicker.Mode != LightFlickerMode.None)
            SetLit(ent, flicker, flicker.Lit);
    }

    private void OnBallastStartup(Entity<DamagedBallastComponent> ent, ref ComponentStartup args)
    {
        if (!IsOn(ent) || IsBlinking(ent))
            return;

        StartFault(ent, EnsureComp<LightFlickerComponent>(ent));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.RealTime;
        var query = EntityQueryEnumerator<LightFlickerComponent>();

        while (query.MoveNext(out var uid, out var flicker))
        {
            if (flicker.Mode == LightFlickerMode.None)
                continue;

            // The server switched it off; its own state already has the light dark and the sprite right.
            if (!IsOn(uid))
            {
                flicker.Mode = LightFlickerMode.None;
                continue;
            }

            if (flicker.Mode == LightFlickerMode.Fault && !HasComp<DamagedBallastComponent>(uid))
            {
                Stop(uid, flicker, true);
                continue;
            }

            if (now >= flicker.NextStep)
                Step(uid, flicker, now);
        }
    }

    private void Step(EntityUid uid, LightFlickerComponent flicker, TimeSpan now)
    {
        switch (flicker.Phase)
        {
            case LightFlickerPhase.Dark:
                flicker.Phase = LightFlickerPhase.Strike;
                flicker.FlashesLeft = _random.Next(1, 4);
                flicker.NextStep = now;
                PlayFaultSound(uid);
                return;

            case LightFlickerPhase.Strike:
                if (flicker.Lit)
                {
                    SetLit(uid, flicker, false);
                    flicker.NextStep = now + Seconds(0.04f, 0.12f);
                    return;
                }

                SetLit(uid, flicker, true);

                if (--flicker.FlashesLeft > 0)
                {
                    flicker.NextStep = now + Seconds(0.04f, 0.1f);
                    return;
                }

                // The last flash catches and stays lit.
                if (flicker.Mode == LightFlickerMode.Strike)
                {
                    flicker.Mode = LightFlickerMode.None;
                    return;
                }

                flicker.Phase = LightFlickerPhase.Hold;
                flicker.NextStep = now + Seconds(0.5f, 1f);
                return;

            case LightFlickerPhase.Hold:
                flicker.Phase = LightFlickerPhase.Dark;
                SetLit(uid, flicker, false);
                flicker.NextStep = now + Seconds(0.5f, 5f);
                return;
        }
    }

    private void StartStrike(EntityUid uid, LightFlickerComponent flicker)
    {
        flicker.Mode = LightFlickerMode.Strike;
        flicker.Phase = LightFlickerPhase.Strike;
        flicker.FlashesLeft = _random.Next(2, 4);
        SetLit(uid, flicker, false);
        flicker.NextStep = _timing.RealTime + Seconds(0.05f, 0.15f);
    }

    private void StartFault(EntityUid uid, LightFlickerComponent flicker)
    {
        if (flicker.Mode == LightFlickerMode.Fault)
            return;

        flicker.Mode = LightFlickerMode.Fault;
        flicker.Phase = LightFlickerPhase.Dark;
        SetLit(uid, flicker, false);
        flicker.NextStep = _timing.RealTime + Seconds(0.2f, 1f);
    }

    /// <summary>Ends any flicker, leaving the light lit if it should be.</summary>
    private void Stop(EntityUid uid, LightFlickerComponent flicker, bool lit)
    {
        if (flicker.Mode == LightFlickerMode.None)
            return;

        flicker.Mode = LightFlickerMode.None;

        if (lit)
            SetLit(uid, flicker, true);
    }

    private void SetLit(EntityUid uid, LightFlickerComponent flicker, bool lit)
    {
        flicker.Lit = lit;
        _pointLight.SetEnabled(uid, lit);

        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        if (_sprite.LayerExists((uid, sprite), PoweredLightLayers.Glow))
            _sprite.LayerSetVisible((uid, sprite), PoweredLightLayers.Glow, lit);

        if (!TryComp<PoweredLightVisualsComponent>(uid, out var visuals))
            return;

        var states = visuals.SpriteStateMap;
        if (states.TryGetValue(lit ? PoweredLightState.On : PoweredLightState.Off, out var state))
            _sprite.LayerSetRsiState((uid, sprite), PoweredLightLayers.Base, state);
    }

    private void PlayFaultSound(EntityUid uid)
    {
        if (!TryComp<PoweredLightComponent>(uid, out var light))
            return;

        var sound = light.TurnOnSound;
        var audioParams = sound.Params
            .AddVolume(FaultSoundVolume)
            .WithMaxDistance(FaultSoundRange)
            .WithPitchScale(_random.NextFloat(0.9f, 1.1f));

        _audio.PlayEntity(sound, Filter.Local(), uid, false, audioParams);
    }

    private bool IsOn(EntityUid uid)
    {
        return _appearance.TryGetData<PoweredLightState>(uid, PoweredLightVisuals.BulbState, out var state)
               && state == PoweredLightState.On;
    }

    /// <summary>A ghost's boo is blinking the light; upstream's animation owns it until that ends.</summary>
    private bool IsBlinking(EntityUid uid)
    {
        return _appearance.TryGetData<bool>(uid, PoweredLightVisuals.Blinking, out var blinking) && blinking;
    }

    private TimeSpan Seconds(float min, float max)
    {
        return TimeSpan.FromSeconds(_random.NextFloat(min, max));
    }
}
