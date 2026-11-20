using System.Linq;
using Content.Shared._WG.MentorHelp;
using Robust.Shared.Network;

namespace Content.Client._WG.MentorHelp;

/// <summary>What the server tells this client about Mentor Help: its own ticket and, as a responder, the queue.</summary>
public sealed class WGClientMentorHelpSystem : EntitySystem
{
    /// <summary>This player's latest ticket, or null.</summary>
    public MentorHelpPlayerTicket? Own { get; private set; }

    /// <summary>Whether this player answers tickets (on-duty mentor or staff).</summary>
    public bool Responder { get; private set; }

    /// <summary>Whether this player sees tickets as staff.</summary>
    public bool Staff { get; private set; }

    /// <summary>The tickets this player may see as a responder, by id.</summary>
    public IReadOnlyDictionary<int, MentorHelpTicketView> Tickets => _tickets;

    /// <summary>Raised when anything here changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when a responder wrote in this player's own ticket.</summary>
    public event Action? OwnReply;

    /// <summary>Raised when a ticket this responder sees is new or has a new message.</summary>
    public event Action? QueueAlert;

    /// <summary>Raised with a notice from the server.</summary>
    public event Action<string>? Notice;

    private readonly Dictionary<int, MentorHelpTicketView> _tickets = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMentorHelpPlayerStateEvent>(OnPlayerState);
        SubscribeNetworkEvent<WGMentorHelpSnapshotEvent>(OnSnapshot);
        SubscribeNetworkEvent<WGMentorHelpTicketEvent>(OnTicket);
        SubscribeNetworkEvent<WGMentorHelpTicketRemovedEvent>(OnRemoved);
        SubscribeNetworkEvent<WGMentorHelpNoticeEvent>(OnNotice);
    }

    public void Open(string text, string? topic, bool secret)
    {
        RaiseNetworkEvent(new WGMentorHelpOpenRequest { Text = text, Topic = topic, Secret = secret });
    }

    /// <summary>Posts in a ticket; null for this player's own.</summary>
    public void Post(int? ticket, string text)
    {
        RaiseNetworkEvent(new WGMentorHelpPostRequest { TicketId = ticket, Text = text });
    }

    public void Act(int ticket, MentorHelpAction action, string? note = null, string? target = null)
    {
        RaiseNetworkEvent(new WGMentorHelpActionRequest { TicketId = ticket, Action = action, Note = note, Target = target });
    }

    public void PlayerAct(MentorHelpPlayerAction action, bool up = false, string? comment = null)
    {
        RaiseNetworkEvent(new WGMentorHelpPlayerActionRequest { Action = action, Up = up, Comment = comment });
    }

    public void Refresh()
    {
        RaiseNetworkEvent(new WGMentorHelpRefreshRequest());
    }

    /// <summary>Staff: moves a player's ahelp to Mentor Help.</summary>
    public void SendFromAHelp(NetUserId player)
    {
        RaiseNetworkEvent(new WGMentorHelpFromAHelpRequest { Player = player });
    }

    private void OnPlayerState(WGMentorHelpPlayerStateEvent ev)
    {
        var before = Own?.Id == ev.Ticket?.Id ? Own?.Lines.Count(l => !l.Own) ?? 0 : 0;
        var after = ev.Ticket?.Lines.Count(l => !l.Own) ?? 0;
        Own = ev.Ticket;

        if (after > before)
            OwnReply?.Invoke();

        Changed?.Invoke();
    }

    private void OnSnapshot(WGMentorHelpSnapshotEvent ev)
    {
        Responder = ev.Responder;
        Staff = ev.Staff;
        _tickets.Clear();
        foreach (var ticket in ev.Tickets)
        {
            _tickets[ticket.Id] = ticket;
        }

        Changed?.Invoke();
    }

    private void OnTicket(WGMentorHelpTicketEvent ev)
    {
        _tickets[ev.Ticket.Id] = ev.Ticket;
        if (ev.Alert && ev.Ticket.Lines.LastOrDefault() is { Own: false })
            QueueAlert?.Invoke();

        Changed?.Invoke();
    }

    private void OnRemoved(WGMentorHelpTicketRemovedEvent ev)
    {
        if (_tickets.Remove(ev.Id))
            Changed?.Invoke();
    }

    private void OnNotice(WGMentorHelpNoticeEvent ev)
    {
        Notice?.Invoke(ev.Text);
    }
}
