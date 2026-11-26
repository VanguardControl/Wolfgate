using System.Linq;
using Content.Shared._WG.AHelpTickets;
using Robust.Shared.Network;

namespace Content.Client._WG.AHelpTickets;

/// <summary>Keeps the round's tickets for the staff panel and the player's own ticket status, and sends staff actions.</summary>
public sealed class AHelpTicketClientSystem : EntitySystem
{
    private readonly Dictionary<int, AHelpTicketInfo> _tickets = new();
    private readonly Dictionary<NetUserId, AHelpTicketPlayerCardMessage> _cards = new();
    private readonly Dictionary<NetUserId, (int AfterKey, string Macro, AHelpTicketAction? Action)> _pendingMacros = new();

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
    /// Records the macro and applies its action to the player's next ticket newer than <paramref name="afterKey"/>,
    /// the one its reply opens. Kept here rather than in the panel, so a rebuilt panel doesn't drop it.
    /// </summary>
    public void QueueMacro(NetUserId player, int afterKey, string macro, AHelpTicketAction? action)
    {
        _pendingMacros[player] = (afterKey, macro, action);
        Log.Debug($"Macro {macro} waits for a ticket of {player} after key {afterKey}");
    }

    private void ApplyPendingMacros()
    {
        foreach (var (player, (afterKey, macro, action)) in _pendingMacros.ToList())
        {
            if (LatestFor(player) is not { } ticket || ticket.Key <= afterKey)
                continue;

            Log.Debug($"Macro {macro} applies to ticket key {ticket.Key} ({ticket.State}) of {player}: {action}");
            _pendingMacros.Remove(player);
            MacroUsed(ticket.Key, macro);
            if (action is { } act)
                Act(ticket.Key, act);
        }
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

        ApplyPendingMacros();
        TicketsChanged?.Invoke(null);
    }

    private void OnUpdated(AHelpTicketUpdatedMessage msg)
    {
        _tickets[msg.Ticket.Key] = msg.Ticket;
        ApplyPendingMacros();
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
