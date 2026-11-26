using Content.Shared._WG.AHelpTickets;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client._WG.AHelpTickets;

/// <summary>Keeps the round's tickets for the staff panel and the player's own ticket status, and sends staff actions.</summary>
public sealed class AHelpTicketClientSystem : EntitySystem
{
    private readonly Dictionary<int, AHelpTicketInfo> _tickets = new();
    private readonly Dictionary<NetUserId, AHelpTicketPlayerCardMessage> _cards = new();
    private readonly Dictionary<NetUserId, (TimeSpan Until, string Macro, AHelpTicketAction? Action)> _pendingMacros = new();

    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How long a macro waits for the ticket its reply lands on.</summary>
    private static readonly TimeSpan MacroWait = TimeSpan.FromSeconds(15);

    /// <summary>This round's tickets, by key.</summary>
    public IReadOnlyDictionary<int, AHelpTicketInfo> Tickets => _tickets;

    /// <summary>The local player's own ticket, as the player sees it.</summary>
    public AHelpTicketPlayerStatusMessage? OwnStatus { get; private set; }

    /// <summary>The list changed; the ticket is null when the whole list was replaced.</summary>
    public event Action<AHelpTicketInfo?>? TicketsChanged;

    public event Action<AHelpTicketPlayerCardMessage>? CardReceived;
    public event Action<AHelpTicketTranscriptMessage>? TranscriptReceived;
    public event Action<AHelpTicketSearchResultsMessage>? SearchResultsReceived;
    public event Action<AHelpTicketPlayerStatusMessage>? OwnStatusChanged;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<AHelpTicketListMessage>(OnList);
        SubscribeNetworkEvent<AHelpTicketUpdatedMessage>(OnUpdated);
        SubscribeNetworkEvent<AHelpTicketPlayerCardMessage>(OnCard);
        SubscribeNetworkEvent<AHelpTicketTranscriptMessage>(msg => TranscriptReceived?.Invoke(msg));
        SubscribeNetworkEvent<AHelpTicketSearchResultsMessage>(msg => SearchResultsReceived?.Invoke(msg));
        SubscribeNetworkEvent<AHelpTicketPlayerStatusMessage>(OnOwnStatus);
    }

    /// <summary>The newest ticket of a player this round.</summary>
    public AHelpTicketInfo? LatestFor(NetUserId player)
    {
        AHelpTicketInfo? latest = null;
        foreach (var ticket in _tickets.Values)
        {
            if (ticket.Player == player && (latest == null || ticket.Key > latest.Key))
                latest = ticket;
        }

        return latest;
    }

    /// <summary>The player's open ticket this round, whatever its age (an older one may have been reopened).</summary>
    public AHelpTicketInfo? OpenFor(NetUserId player)
    {
        AHelpTicketInfo? open = null;
        foreach (var ticket in _tickets.Values)
        {
            if (ticket.Player == player && !ticket.State.IsClosed() && (open == null || ticket.Key > open.Key))
                open = ticket;
        }

        return open;
    }

    /// <summary>The player card last received, if any.</summary>
    public AHelpTicketPlayerCardMessage? Card(NetUserId player)
    {
        return _cards.GetValueOrDefault(player);
    }

    public void Act(int key, AHelpTicketAction action)
    {
        RaiseNetworkEvent(new AHelpTicketActionMessage(key, action));
    }

    public void MacroUsed(int key, string macro)
    {
        RaiseNetworkEvent(new AHelpTicketMacroUsedMessage(key, macro));
    }

    /// <summary>
    /// For a macro whose reply went to a player with no open ticket the client knows of: records the macro and
    /// applies its action to the ticket the reply lands on, which is the player's next ticket update. A reply that
    /// lands nowhere within <see cref="MacroWait"/> is dropped, so the action can't reach a later, unrelated ticket.
    /// </summary>
    public void QueueMacro(NetUserId player, string macro, AHelpTicketAction? action)
    {
        _pendingMacros[player] = (_timing.RealTime + MacroWait, macro, action);
    }

    /// <summary>Applies a waiting macro to the ticket that just changed, if it is that player's and still in time.</summary>
    private void ApplyPendingMacro(AHelpTicketInfo ticket)
    {
        if (!_pendingMacros.Remove(ticket.Player, out var pending) || _timing.RealTime > pending.Until)
            return;

        MacroUsed(ticket.Key, pending.Macro);
        if (pending.Action is { } action)
            Act(ticket.Key, action);
    }

    public void RequestCard(NetUserId player)
    {
        RaiseNetworkEvent(new AHelpTicketPlayerCardRequest(player));
    }

    public void RequestTranscript(int number)
    {
        RaiseNetworkEvent(new AHelpTicketTranscriptRequest(number));
    }

    public void Search(string text)
    {
        RaiseNetworkEvent(new AHelpTicketSearchRequest(text));
    }

    private void OnList(AHelpTicketListMessage msg)
    {
        _tickets.Clear();
        foreach (var ticket in msg.Tickets)
        {
            _tickets[ticket.Key] = ticket;
        }

        TicketsChanged?.Invoke(null);
    }

    private void OnUpdated(AHelpTicketUpdatedMessage msg)
    {
        _tickets[msg.Ticket.Key] = msg.Ticket;
        ApplyPendingMacro(msg.Ticket);
        TicketsChanged?.Invoke(msg.Ticket);
    }

    private void OnCard(AHelpTicketPlayerCardMessage msg)
    {
        _cards[msg.Player] = msg;
        CardReceived?.Invoke(msg);
    }

    private void OnOwnStatus(AHelpTicketPlayerStatusMessage msg)
    {
        OwnStatus = msg;
        OwnStatusChanged?.Invoke(msg);
    }
}
