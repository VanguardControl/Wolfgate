using System.Numerics;
using Content.Shared._WG.AHelpTickets;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Client._WG.AHelpTickets;

/// <summary>Stored tickets: a player's history or a search, and the transcript of the one picked.</summary>
public sealed class AHelpTicketHistoryWindow : DefaultWindow
{
    [Dependency] private IEntityManager _entities = default!;

    private readonly AHelpTicketClientSystem _tickets;
    private readonly Label _heading = new() { StyleClasses = { "LabelHeading" } };
    private readonly BoxContainer _entries = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly OutputPanel _transcript = new() { VerticalExpand = true, HorizontalExpand = true };

    private NetUserId? _player;
    private string? _search;

    public AHelpTicketHistoryWindow()
    {
        IoCManager.InjectDependencies(this);
        _tickets = _entities.System<AHelpTicketClientSystem>();

        Title = Loc.GetString("wg-ahelp-history-title");
        MinSize = new Vector2(760, 420);

        var list = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, MinWidth = 320 };
        list.AddChild(_entries);

        var left = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, MinWidth = 320 };
        left.AddChild(_heading);
        left.AddChild(list);

        var split = new SplitContainer { Orientation = SplitContainer.SplitOrientation.Horizontal, VerticalExpand = true };
        split.AddChild(left);
        split.AddChild(_transcript);
        Contents.AddChild(split);

        _tickets.CardReceived += OnCard;
        _tickets.SearchResultsReceived += OnSearchResults;
        _tickets.TranscriptReceived += OnTranscript;
    }

    /// <summary>Lists a player's past tickets.</summary>
    public void ShowPlayer(NetUserId player, string username)
    {
        _player = player;
        _search = null;
        _heading.Text = $"{Loc.GetString("wg-ahelp-history-title")}: {username}";
        _transcript.Clear();
        if (_tickets.Card(player) is { } card)
            ShowEntries(card.Previous);
        else
            ShowEntries(new List<AHelpTicketHistoryEntry>());

        _tickets.RequestCard(player);
    }

    /// <summary>Lists stored tickets that mention the text.</summary>
    public void ShowSearch(string text)
    {
        _player = null;
        _search = text;
        _heading.Text = Loc.GetString("wg-ahelp-history-search-title", ("text", text));
        _transcript.Clear();
        ShowEntries(new List<AHelpTicketHistoryEntry>());
        _tickets.Search(text);
    }

    private void OnCard(AHelpTicketPlayerCardMessage card)
    {
        if (card.Player == _player)
            ShowEntries(card.Previous);
    }

    private void OnSearchResults(AHelpTicketSearchResultsMessage results)
    {
        if (results.Text == _search)
            ShowEntries(results.Results);
    }

    private void ShowEntries(List<AHelpTicketHistoryEntry> entries)
    {
        _entries.RemoveAllChildren();
        if (entries.Count == 0)
        {
            _entries.AddChild(new Label { Text = Loc.GetString("wg-ahelp-history-empty"), StyleClasses = { "LabelSubText" } });
            return;
        }

        foreach (var entry in entries)
        {
            var button = new Button { ClipText = true, HorizontalExpand = true, ToolTip = entry.Snippet };
            button.Text = Loc.GetString("wg-ahelp-history-entry",
                ("number", entry.Number),
                ("round", entry.RoundId),
                ("username", entry.Username),
                ("state", Loc.GetString($"wg-ahelp-tickets-state-{entry.State}")),
                ("date", entry.Opened.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));
            var number = entry.Number;
            button.OnPressed += _ => _tickets.RequestTranscript(number);
            _entries.AddChild(button);
        }
    }

    private void OnTranscript(AHelpTicketTranscriptMessage msg)
    {
        _transcript.Clear();
        foreach (var line in msg.Lines)
        {
            var markup = Loc.GetString("wg-ahelp-history-line",
                ("time", line.SentAt.ToLocalTime().ToString("HH:mm")),
                ("source", line.Source),
                ("author", FormattedMessage.EscapeText(line.Author)),
                ("adminOnly", line.AdminOnly),
                ("text", FormattedMessage.EscapeText(line.Text)));
            var message = new FormattedMessage();
            if (!message.TryAddMarkup(markup, out _))
                message.AddText($"{line.Author}: {line.Text}");
            _transcript.AddMessage(message);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _tickets.CardReceived -= OnCard;
        _tickets.SearchResultsReceived -= OnSearchResults;
        _tickets.TranscriptReceived -= OnTranscript;
    }
}
