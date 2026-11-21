using System.Threading.Tasks;
using Content.Server.Discord;
using Content.Shared._WG.MentorHelp;
using Robust.Shared.Configuration;

namespace Content.Server._WG.MentorHelp;

/// <summary>
/// Copies Mentor Help ticket lines to the mentors' Discord channel through <c>wg.mentor.discord_webhook</c>. One way,
/// no pings; the asker appears by character name only.
/// </summary>
public sealed class WGMentorHelpDiscordSystem : EntitySystem
{
    private const int MaxContentLength = 2000;

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private DiscordWebhook _discord = default!;

    private string _url = string.Empty;
    private WebhookIdentifier? _identifier;

    // Lines go out one after another, so a ticket reads in order.
    private Task _sending = Task.CompletedTask;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, MentorHelpCVars.DiscordWebhook, url =>
        {
            _url = url;
            _identifier = null;
        }, true);
    }

    // Spoiler-safe tickets stay out of the channel entirely: mentors in the round can read it.
    public void Opened(MentorTicket ticket)
    {
        if (ticket.Secret)
            return;

        var topic = ticket.Topic != null ? Loc.GetString($"wg-mhelp-topic-{ticket.Topic}") : Loc.GetString("wg-mhelp-topic-other");
        Send(Loc.GetString("wg-mhelp-discord-opened",
            ("ticket", ticket.Id), ("character", ticket.CharacterName), ("topic", topic)));
    }

    public void Line(MentorTicket ticket, MentorTicketLine line)
    {
        if (line.RespondersOnly || ticket.Secret)
            return;

        var sender = line.Role == MentorSenderRole.Player ? ticket.CharacterName : line.SenderName;
        Send(Loc.GetString("wg-mhelp-discord-line", ("ticket", ticket.Id), ("sender", sender), ("message", line.Text)));
    }

    public void Closed(MentorTicket ticket)
    {
        if (ticket.Secret)
            return;

        var reason = ticket.CloseReason is { } r ? WGMentorHelpSystem.ReasonToDb(r) : string.Empty;
        Send(Loc.GetString("wg-mhelp-discord-closed", ("ticket", ticket.Id), ("reason", reason)));
    }

    private void Send(string text)
    {
        if (string.IsNullOrWhiteSpace(_url))
            return;

        var url = _url;
        var previous = _sending;
        _sending = SendAsync(previous, url, text);
    }

    private async Task SendAsync(Task previous, string url, string text)
    {
        try
        {
            await previous;
            if (_identifier == null)
            {
                if (await _discord.GetWebhook(url) is not { } data || url != _url)
                    return;

                _identifier = data.ToIdentifier();
            }

            await _discord.CreateMessage(_identifier.Value, new WebhookPayload
            {
                Content = text.Length <= MaxContentLength ? text : text[..MaxContentLength],
            });
        }
        catch (Exception e)
        {
            Log.Error($"Failed to copy Mentor Help to Discord: {e}");
        }
    }
}
