using System;
using System.Collections.Generic;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Client._WF.Audio;

/// <summary>
/// Bounds network sound allocation before engine audio startup. Large impacts may deliver hundreds
/// of different clips in one state, so a per-file concurrency limit is insufficient.
/// </summary>
public sealed partial class WFAudioBudgetSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public const int MaxStreams = 96;
    private readonly HashSet<EntityUid> _admitted = new();
    private readonly Queue<TimeSpan> _retiring = new();
    public int Suppressed { get; private set; }
    public int Reserved => _admitted.Count + _retiring.Count;

    public override void Initialize()
    {
        SubscribeLocalEvent<AudioComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AudioComponent, ComponentRemove>(OnRemove);
    }

    private void OnInit(EntityUid uid, AudioComponent component, ComponentInit args)
    {
        while (_retiring.TryPeek(out var until) && until <= _timing.RealTime)
            _retiring.Dequeue();

        // Local streams are already allocated before ComponentInit. Count them against the network
        // budget; leave headroom for UI/music and other engine audio that does not use AudioComponent.
        if (IsClientSide(uid) || Reserved < MaxStreams)
        {
            _admitted.Add(uid);
            return;
        }

        // WOLFGATE: use the engine's existing silent source and Loaded startup guard. This is local
        // admission only: never alter the server's audio state or despawn a network-owned entity.
#pragma warning disable RA0002 // Deliberate content-side allocation guard; no engine changes.
        component.Loaded = true;
#pragma warning restore RA0002
        Suppressed++;
    }

    private void OnRemove(EntityUid uid, AudioComponent component, ComponentRemove args)
    {
        if (_admitted.Remove(uid))
            // OpenAL disposal is deferred. A state batch can delete and create many sounds before
            // the next audio frame, so do not immediately reuse all of those reservations.
            _retiring.Enqueue(_timing.RealTime + TimeSpan.FromSeconds(0.25));
    }

    public override void Shutdown()
    {
        _admitted.Clear();
        _retiring.Clear();
        base.Shutdown();
    }
}
