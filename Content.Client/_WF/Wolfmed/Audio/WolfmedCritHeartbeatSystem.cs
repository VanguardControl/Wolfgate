using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Player;

namespace Content.Client._WF.Wolfmed.Audio;

/// <summary>
/// Plays a looping heartbeat while the local player's own body is in critical condition. Never plays for
/// anyone else's mob and works for any entity with MobStateComponent, not only wound hosts.
/// </summary>
[UsedImplicitly]
public sealed class WolfmedCritHeartbeatSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private const string HeartbeatSound = "/Audio/_WF/Wolfmed/heartbeat_loop.ogg";

    private EntityUid? _stream;
    private bool _cvarEnabled = true;

    /// <summary>
    /// True whenever the loop should be playing, even if the headless audio backend returned no stream
    /// (e.g. in integration tests).
    /// </summary>
    public bool Active { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        // MobStateComponent has no ComponentHandleState of its own (AutoGenerateComponentState), so a state
        // change made purely on the server (the common case: wound/threshold crit is server-only) never raises
        // MobStateChangedEvent on the client. AfterAutoHandleStateEvent fires whenever the networked fields are
        // applied client-side and is the reliable signal here.
        SubscribeLocalEvent<MobStateComponent, AfterAutoHandleStateEvent>(OnMobStateNetworked);
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);

        Subs.CVar(_cfg, WolfmedCVars.CritHeartbeat, OnCVarChanged, true);
    }

    private void OnCVarChanged(bool value)
    {
        _cvarEnabled = value;
        Refresh();
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        Refresh();
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        Stop();
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.Target != _player.LocalEntity)
            return;

        Refresh();
    }

    private void OnMobStateNetworked(EntityUid uid, MobStateComponent component, ref AfterAutoHandleStateEvent args)
    {
        if (uid != _player.LocalEntity)
            return;

        Refresh();
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        if (args.Entity.Owner != _player.LocalEntity)
            return;

        Stop();
    }

    /// <summary>Re-checks the local entity's mob state and starts or stops the loop to match.</summary>
    private void Refresh()
    {
        if (_cvarEnabled &&
            _player.LocalEntity is { } local &&
            TryComp<MobStateComponent>(local, out var mobState) &&
            mobState.CurrentState == MobState.Critical)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void Start()
    {
        Active = true;
        if (_stream != null)
            return;

        var played = _audio.PlayGlobal(HeartbeatSound, Filter.Local(), false,
            AudioParams.Default.WithLoop(true).WithVolume(-4f));
        _stream = played?.Entity;
    }

    private void Stop()
    {
        Active = false;
        if (_stream == null)
            return;

        _audio.Stop(_stream);
        _stream = null;
    }
}
