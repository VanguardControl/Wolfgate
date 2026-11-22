using Content.Shared._WG.MentorObserve;
using Robust.Shared.Input.Binding;
using Robust.Shared.Timing;

namespace Content.Client._WG.MentorObserve;

/// <summary>
/// The client side of mentors watching and critters: the asker's prompt and state, the mentor's own visit, the return
/// key (Shift+F3), and the ping marker a critter shows the asker.
/// </summary>
public sealed class WGClientMentorObserveSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public const string PingMarker = "WGMentorPingMarker";

    /// <summary>A mentor's request waiting for this player's answer.</summary>
    public WGMentorVisitPromptEvent? Prompt { get; private set; }

    public WGMentorVisitStateEvent State { get; private set; } = new();

    /// <summary>Raised when the prompt or the state changes.</summary>
    public event Action? Changed;

    private readonly List<(EntityUid Marker, TimeSpan Until)> _markers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMentorVisitPromptEvent>(OnPrompt);
        SubscribeNetworkEvent<WGMentorVisitStateEvent>(OnState);
        SubscribeNetworkEvent<WGMentorPingEvent>(OnPing);

        CommandBinds.Builder
            .Bind(WGMentorObserveKeys.WGMentorReturn, InputCmdHandler.FromDelegate(_ => Return(), handle: true, outsidePrediction: true))
            .Register<WGClientMentorObserveSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGClientMentorObserveSystem>();
    }

    private void OnPrompt(WGMentorVisitPromptEvent ev)
    {
        Prompt = ev;
        Changed?.Invoke();
    }

    private void OnState(WGMentorVisitStateEvent ev)
    {
        State = ev;
        Changed?.Invoke();
    }

    /// <summary>Shows where the critter pointed, for this player alone, for a few seconds.</summary>
    private void OnPing(WGMentorPingEvent ev)
    {
        var coords = GetCoordinates(ev.Coordinates);
        if (!coords.IsValid(EntityManager))
            return;

        _markers.Add((Spawn(PingMarker, coords), _timing.CurTime + MentorObserveRules.PingTime));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var now = _timing.CurTime;
        for (var i = _markers.Count - 1; i >= 0; i--)
        {
            if (_markers[i].Until > now)
                continue;

            QueueDel(_markers[i].Marker);
            _markers.RemoveAt(i);
        }
    }

    public void Request(int ticket, MentorVisitKind kind, string? critter)
    {
        RaiseNetworkEvent(new WGMentorVisitRequestEvent { TicketId = ticket, Kind = kind, Critter = critter });
    }

    public void Answer(bool allow)
    {
        if (Prompt is not { } prompt)
            return;

        RaiseNetworkEvent(new WGMentorVisitAnswerEvent { TicketId = prompt.TicketId, Allow = allow });
        Prompt = null;
        Changed?.Invoke();
    }

    /// <summary>As the asker: stop every visit, or only take the critter out.</summary>
    public void EndVisits(bool takeOutOnly)
    {
        RaiseNetworkEvent(new WGMentorVisitEndEvent { TakeOutOnly = takeOutOnly });
    }

    /// <summary>As the mentor: go back to the body, or stop following.</summary>
    public void Return()
    {
        RaiseNetworkEvent(new WGMentorReturnRequestEvent());
    }
}
