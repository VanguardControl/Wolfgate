using Content.Shared._WG.AHelpTickets;
using Robust.Shared.Network;

namespace Content.Client._WG.AHelpTickets;

/// <summary>Keeps the round's tickets for the staff panel and the player's own ticket status, and sends staff actions.</summary>
public sealed class AHelpTicketClientSystem : EntitySystem
{
    private readonly Dictionary<int, AHelpTicketInfo> _tickets = new();
    private readonly Dictionary<NetUserId, AHelpTicketPlayerCardMessage> _cards = new();

    /// <summary>The ticket last reported as viewed.</summary>
    private int? _viewing;

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
    /// Sends a macro's reply. The server sends it on the staffer's behalf and applies the macro to the ticket the
    /// reply lands on, whether it opens one or joins one, so nothing here has to guess which ticket that is.
    /// </summary>
    public void SendMacroReply(NetUserId player, string text, bool playSound, bool adminOnly, string macro, AHelpTicketAction? action)
    {
        RaiseNetworkEvent(new AHelpTicketMacroReplyMessage(player, text, playSound, adminOnly, macro, action));
    }

    public void RequestCard(NetUserId player)
    {
        RaiseNetworkEvent(new AHelpTicketPlayerCardRequest(player));
    }

    /// <summary>Tells the server which ticket this staffer has open, so others see who is looking at it.</summary>
    public void View(int? key)
    {
        if (key == _viewing)
            return;

        _viewing = key;
        RaiseNetworkEvent(new AHelpTicketViewMessage(key));
    }

    /// <summary>Asks the other staff online to look at a ticket.</summary>
    public void Assist(int key)
    {
        RaiseNetworkEvent(new AHelpTicketAssistMessage(key));
    }

    /// <summary>Gives the player their job loadout again, free or paid from their bank.</summary>
    public void GiveLoadout(NetUserId player, bool charge)
    {
        RaiseNetworkEvent(new AHelpTicketLoadoutMessage(player, charge));
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
        // A fresh list follows a reconnect or round end, when the server has forgotten what this staffer was viewing.
        _viewing = null;
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
