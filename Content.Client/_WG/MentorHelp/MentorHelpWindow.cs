using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.MentorHelp;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WG.MentorHelp;

/// <summary>Everything the Mentor Help window shows, rebuilt from the client systems on each change.</summary>
public sealed class MentorHelpWindowState
{
    public MentorHelpPlayerTicket? Own;
    public bool Responder;
    public bool Staff;
    public bool IsMentor;
    public bool OnDuty;
    public bool StartOffDuty;
    public bool ShowCrown;
    public int MentorsOnDuty;
    public IEnumerable<MentorHelpTicketView> Tickets = Array.Empty<MentorHelpTicketView>();
}

/// <summary>
/// The Mentor Help window: the player's own question (ask, reply, end, rate, reopen) and, for responders, the queue
/// of open questions with the actions on the selected one.
/// </summary>
public sealed class MentorHelpWindow : DefaultWindow
{
    public event Action<string, string?, bool>? OpenPressed;
    public event Action<int?, string>? PostPressed;
    public event Action<int, MentorHelpAction, string?, string?>? ActionPressed;
    public event Action<MentorHelpPlayerAction, bool, string?>? PlayerActionPressed;
    public event Action? AskStaffPressed;
    public event Action<bool>? DutyToggled;
    public event Action<bool>? StartOffDutyToggled;
    public event Action<bool>? CrownToggled;

    private readonly Label _onDuty = new();
    private readonly TabContainer _tabs = new() { VerticalExpand = true };
    private readonly Label _notice = new() { FontColorOverride = Color.Khaki };

    // The player's side.
    private readonly BoxContainer _form = Column();
    private readonly OptionButton _topic = new() { HorizontalExpand = true };
    private readonly CheckBox _secret = new();
    private readonly LineEdit _question = new() { HorizontalExpand = true };
    private readonly BoxContainer _noneOnDuty = Column();
    private readonly BoxContainer _ownBox = Column();
    private readonly RichTextLabel _ownStatus = new();
    private readonly BoxContainer _ownLines = Column();
    private readonly BoxContainer _ownReplyRow = Row();
    private readonly LineEdit _ownReply = new() { HorizontalExpand = true };
    private readonly Button _end = new();
    private readonly BoxContainer _rateBox = Column();
    private readonly LineEdit _rateComment = new() { HorizontalExpand = true };
    private readonly BoxContainer _reopenBox = Row();
    private readonly Label _reopenHint = new();
    private readonly ConfirmButton _report = new();

    // The responder's side.
    private readonly BoxContainer _dutyRow = Row();
    private readonly CheckBox _duty = new();
    private readonly CheckBox _startOff = new();
    private readonly CheckBox _crown = new();
    private readonly Label _staffMode = new();
    private readonly BoxContainer _queue = Column();
    private readonly RichTextLabel _ticketHeader = new();
    private readonly BoxContainer _ticketLines = Column();
    private readonly BoxContainer _ticketActions = Row();
    private readonly LineEdit _note = new() { HorizontalExpand = true };
    private readonly LineEdit _target = new() { HorizontalExpand = true };
    private readonly BoxContainer _ticketReplyRow = Row();
    private readonly LineEdit _ticketReply = new() { HorizontalExpand = true };

    private int? _selected;
    private MentorHelpWindowState _state = new();

    public MentorHelpWindow()
    {
        Title = Loc.GetString("wg-mhelp-window-title");
        MinSize = new Vector2(560, 520);
        SetSize = new Vector2(640, 600);

        var root = Column();
        root.Margin = new Thickness(6);
        root.AddChild(_onDuty);
        root.AddChild(_tabs);
        root.AddChild(_notice);
        Contents.AddChild(root);

        _tabs.AddChild(BuildAsk());
        _tabs.AddChild(BuildQueue());
        _tabs.SetTabTitle(0, Loc.GetString("wg-mhelp-window-title"));
        _tabs.SetTabTitle(1, Loc.GetString("wg-mhelp-queue-title"));
    }

    /// <summary>Fills the question box, as when a how-to question comes over from the ahelp.</summary>
    public void SetQuestion(string text)
    {
        _question.Text = text;
        _tabs.CurrentTab = 0;
    }

    /// <summary>Shows a notice from the server under the tabs.</summary>
    public void ShowNotice(string text)
    {
        _notice.Text = text;
    }

    public void SetState(MentorHelpWindowState state)
    {
        _state = state;
        _onDuty.Text = Loc.GetString("wg-mhelp-on-duty", ("count", state.MentorsOnDuty));

        UpdateOwn();

        var showQueue = state.Responder || state.IsMentor;
        _tabs.SetTabVisible(1, showQueue);
        if (!showQueue)
            _tabs.CurrentTab = 0;

        UpdateQueue();
    }

    #region Building

    private Control BuildAsk()
    {
        var page = Column();
        page.Margin = new Thickness(4);

        var intro = new RichTextLabel();
        intro.SetMessage(Loc.GetString("wg-mhelp-intro"));
        page.AddChild(intro);

        // The form for a new question.
        var topicRow = Row();
        topicRow.AddChild(new Label { Text = Loc.GetString("wg-mhelp-topic-label"), MinWidth = 80 });
        foreach (var topic in MentorHelpTopics.All)
        {
            _topic.AddItem(Loc.GetString($"wg-mhelp-topic-{topic}"));
        }

        _topic.OnItemSelected += args => _topic.SelectId(args.Id);
        _topic.SelectId(MentorHelpTopics.All.Length - 1);
        topicRow.AddChild(_topic);
        _form.AddChild(topicRow);

        _secret.Text = Loc.GetString("wg-mhelp-secret-box");
        _secret.ToolTip = Loc.GetString("wg-mhelp-secret-hint");
        _form.AddChild(_secret);

        var askRow = Row();
        _question.PlaceHolder = Loc.GetString("wg-mhelp-question-placeholder");
        _question.OnTextEntered += _ => Ask();
        askRow.AddChild(_question);
        askRow.AddChild(Button("wg-mhelp-send", Ask));
        _form.AddChild(askRow);

        var none = new RichTextLabel();
        none.SetMessage(Loc.GetString("wg-mhelp-none-on-duty"));
        _noneOnDuty.AddChild(none);
        _noneOnDuty.AddChild(Button("wg-mhelp-ask-staff", () => AskStaffPressed?.Invoke()));
        _form.AddChild(_noneOnDuty);
        page.AddChild(_form);

        // The open or just-closed ticket.
        _ownBox.AddChild(_ownStatus);
        _ownBox.AddChild(Scroll(_ownLines));

        _ownReply.PlaceHolder = Loc.GetString("wg-mhelp-reply-placeholder");
        _ownReply.OnTextEntered += _ => ReplyOwn();
        _ownReplyRow.AddChild(_ownReply);
        _ownReplyRow.AddChild(Button("wg-mhelp-send", ReplyOwn));
        _ownBox.AddChild(_ownReplyRow);

        _end.Text = Loc.GetString("wg-mhelp-end");
        _end.OnPressed += _ => PlayerActionPressed?.Invoke(MentorHelpPlayerAction.End, false, null);
        _ownBox.AddChild(_end);

        var rateRow = Row();
        rateRow.AddChild(Button("wg-mhelp-rate-up", () => Rate(true)));
        rateRow.AddChild(Button("wg-mhelp-rate-down", () => Rate(false)));
        _rateComment.PlaceHolder = Loc.GetString("wg-mhelp-rate-comment");
        _rateBox.AddChild(rateRow);
        _rateBox.AddChild(_rateComment);
        _ownBox.AddChild(_rateBox);

        _reopenBox.AddChild(Button("wg-mhelp-reopen", () => PlayerActionPressed?.Invoke(MentorHelpPlayerAction.Reopen, false, null)));
        _reopenBox.AddChild(_reopenHint);
        _ownBox.AddChild(_reopenBox);

        _report.Text = Loc.GetString("wg-mhelp-report");
        _report.ConfirmationText = Loc.GetString("wg-mhelp-report-confirm");
        _report.OnPressed += _ => PlayerActionPressed?.Invoke(MentorHelpPlayerAction.Report, false, null);
        _ownBox.AddChild(_report);

        page.AddChild(_ownBox);
        return page;
    }

    private Control BuildQueue()
    {
        var page = Column();
        page.Margin = new Thickness(4);

        _duty.Text = Loc.GetString("wg-mentor-duty-toggle");
        _duty.OnToggled += args => DutyToggled?.Invoke(args.Pressed);
        _startOff.Text = Loc.GetString("wg-mentor-duty-start-off");
        _startOff.OnToggled += args => StartOffDutyToggled?.Invoke(args.Pressed);
        _crown.Text = Loc.GetString("wg-mentor-crown-option");
        _crown.OnToggled += args => CrownToggled?.Invoke(args.Pressed);
        _dutyRow.AddChild(_duty);
        _dutyRow.AddChild(_startOff);
        _dutyRow.AddChild(_crown);
        page.AddChild(_dutyRow);

        _staffMode.Text = Loc.GetString("wg-mhelp-mode-staff");
        page.AddChild(_staffMode);

        var split = Row();
        split.VerticalExpand = true;

        var list = Scroll(_queue);
        list.MinWidth = 200;
        list.HorizontalExpand = false;
        split.AddChild(list);

        var detail = Column();
        detail.HorizontalExpand = true;
        detail.AddChild(_ticketHeader);
        detail.AddChild(Scroll(_ticketLines));

        _note.PlaceHolder = Loc.GetString("wg-mhelp-take-over-note");
        _target.PlaceHolder = Loc.GetString("wg-mhelp-transfer");
        detail.AddChild(_ticketActions);

        _ticketReply.PlaceHolder = Loc.GetString("wg-mhelp-reply-placeholder");
        _ticketReply.OnTextEntered += _ => ReplyTicket();
        _ticketReplyRow.AddChild(_ticketReply);
        _ticketReplyRow.AddChild(Button("wg-mhelp-send", ReplyTicket));
        detail.AddChild(_ticketReplyRow);

        split.AddChild(detail);
        page.AddChild(split);
        return page;
    }

    #endregion

    #region Updating

    private void UpdateOwn()
    {
        var own = _state.Own;
        var open = own is { Status: not MentorTicketStatus.Closed };

        _form.Visible = !open;
        _noneOnDuty.Visible = _state.MentorsOnDuty == 0;
        _ownBox.Visible = own != null;
        if (own == null)
            return;

        _ownStatus.SetMessage(own.Status switch
        {
            MentorTicketStatus.Waiting => Loc.GetString(_state.MentorsOnDuty == 0 ? "wg-mhelp-none-on-duty" : "wg-mhelp-waiting"),
            MentorTicketStatus.Claimed => Loc.GetString("wg-mhelp-claimed"),
            _ when own.CloseReason == MentorCloseReason.Resolved => Loc.GetString("wg-mhelp-resolved"),
            _ => string.Empty,
        });

        FillLines(_ownLines, own.Lines);
        _ownReplyRow.Visible = open;
        _end.Visible = open;
        _rateBox.Visible = !open && own.Answered && !own.Rated;

        _report.Visible = own.Answered && !own.Reported;

        var reopenMinutes = own.ReopenUntil is { } until ? (int) Math.Ceiling((until - DateTime.UtcNow).TotalMinutes) : 0;
        _reopenBox.Visible = !open && reopenMinutes > 0;
        _reopenHint.Text = Loc.GetString("wg-mhelp-reopen-hint", ("minutes", Math.Max(reopenMinutes, 1)));
    }

    private void UpdateQueue()
    {
        _duty.Visible = _state.IsMentor;
        _startOff.Visible = _state.IsMentor;
        _crown.Visible = _state.IsMentor;
        _duty.Pressed = _state.OnDuty;
        _startOff.Pressed = _state.StartOffDuty;
        _crown.Pressed = _state.ShowCrown;
        _staffMode.Visible = _state.Staff;

        _queue.RemoveAllChildren();
        var ordered = _state.Tickets
            .OrderBy(t => t.Status == MentorTicketStatus.Closed)
            .ThenByDescending(t => t.Newbie)
            .ThenBy(t => t.OpenedAt)
            .ToList();

        if (ordered.Count == 0)
            _queue.AddChild(new Label { Text = Loc.GetString("wg-mhelp-queue-empty") });

        foreach (var ticket in ordered)
        {
            var entry = new Button
            {
                Text = QueueEntry(ticket),
                ToggleMode = true,
                Pressed = ticket.Id == _selected,
                ClipText = true,
                ToolTip = ticket.Crew ? Loc.GetString("wg-mhelp-tag-crew-hint") : null,
            };

            var id = ticket.Id;
            entry.OnPressed += _ =>
            {
                _selected = id;
                UpdateQueue();
            };
            _queue.AddChild(entry);
        }

        var selected = _selected is { } sel ? ordered.FirstOrDefault(t => t.Id == sel) : null;
        _ticketHeader.Visible = selected != null;
        _ticketActions.Visible = selected is { Status: not MentorTicketStatus.Closed };
        _ticketReplyRow.Visible = selected is { Status: not MentorTicketStatus.Closed };
        _ticketLines.RemoveAllChildren();
        if (selected == null)
            return;

        var header = new FormattedMessage();
        header.AddText(Asker(selected));
        if (selected.AskerUsername != null)
            header.AddText($" ({selected.AskerUsername})");

        if (selected.ClaimedBy != null)
        {
            header.PushNewline();
            header.AddText(Loc.GetString("wg-mhelp-claimed-by", ("mentor", selected.ClaimedBy)));
        }

        _ticketHeader.SetMessage(header);
        FillLines(_ticketLines, selected.Lines);
        FillActions(selected);
    }

    private void FillActions(MentorHelpTicketView ticket)
    {
        _ticketActions.RemoveAllChildren();
        _note.Orphan();
        _target.Orphan();

        if (ticket.ClaimedBy == null)
        {
            _ticketActions.AddChild(Button("wg-mhelp-claim", () => Act(ticket, MentorHelpAction.Claim)));
        }
        else if (ticket.ClaimedByMe)
        {
            _ticketActions.AddChild(Button("wg-mhelp-unclaim", () => Act(ticket, MentorHelpAction.Unclaim)));
            _ticketActions.AddChild(_target);
            _ticketActions.AddChild(Button("wg-mhelp-transfer", () =>
            {
                if (_target.Text.Trim().Length > 0)
                    Act(ticket, MentorHelpAction.Transfer, target: _target.Text.Trim());
            }));
        }
        else
        {
            _ticketActions.AddChild(_note);
            _ticketActions.AddChild(Button("wg-mhelp-take-over", () => Act(ticket, MentorHelpAction.TakeOver, note: _note.Text)));
        }

        if (ticket.ClaimedBy == null || ticket.ClaimedByMe || _state.Staff)
        {
            _ticketActions.AddChild(Button("wg-mhelp-resolve", () => Act(ticket, MentorHelpAction.Resolve)));

            var escalate = new ConfirmButton
            {
                Text = Loc.GetString("wg-mhelp-escalate"),
                ConfirmationText = Loc.GetString("wg-mhelp-escalate-confirm"),
            };
            escalate.OnPressed += _ => Act(ticket, MentorHelpAction.Escalate);
            _ticketActions.AddChild(escalate);
        }
    }

    private static void FillLines(BoxContainer box, List<MentorHelpLine> lines)
    {
        box.RemoveAllChildren();
        foreach (var line in lines)
        {
            var message = new FormattedMessage();
            if (line.Sender.Length > 0)
            {
                message.PushTag(new MarkupNode("bold", null, null));
                message.AddText(line.Sender);
                message.Pop();
                message.AddText(": ");
            }

            message.AddText(line.Text);
            var label = new RichTextLabel();
            label.SetMessage(message);
            box.AddChild(label);
        }
    }

    private static string QueueEntry(MentorHelpTicketView ticket)
    {
        var parts = new List<string> { Asker(ticket) };
        if (ticket.Newbie)
            parts.Add(Loc.GetString("wg-mhelp-tag-newbie"));

        if (ticket.Secret)
            parts.Add(Loc.GetString("wg-mhelp-tag-secret"));

        if (ticket.Crew)
            parts.Add(Loc.GetString("wg-mhelp-tag-crew"));

        if (ticket.Topic != null)
            parts.Add(Loc.GetString($"wg-mhelp-topic-{ticket.Topic}"));

        var minutes = Math.Max(0, (int) (DateTime.UtcNow - ticket.OpenedAt).TotalMinutes);
        parts.Add(Loc.GetString("wg-mhelp-waited", ("minutes", minutes)));
        return string.Join(" · ", parts);
    }

    private static string Asker(MentorHelpTicketView ticket)
    {
        return Loc.GetString(ticket.AskerInLobby ? "wg-mhelp-asker-lobby" : "wg-mhelp-asker", ("character", ticket.Asker));
    }

    #endregion

    #region Input

    private void Ask()
    {
        var text = _question.Text.Trim();
        if (text.Length == 0)
            return;

        var topic = _topic.SelectedId >= 0 && _topic.SelectedId < MentorHelpTopics.All.Length
            ? MentorHelpTopics.All[_topic.SelectedId]
            : null;

        OpenPressed?.Invoke(text, topic, _secret.Pressed);
        _question.Text = string.Empty;
        _secret.Pressed = false;
    }

    private void ReplyOwn()
    {
        var text = _ownReply.Text.Trim();
        if (text.Length == 0)
            return;

        PostPressed?.Invoke(null, text);
        _ownReply.Text = string.Empty;
    }

    private void ReplyTicket()
    {
        var text = _ticketReply.Text.Trim();
        if (text.Length == 0 || _selected is not { } id)
            return;

        PostPressed?.Invoke(id, text);
        _ticketReply.Text = string.Empty;
    }

    private void Rate(bool up)
    {
        var comment = _rateComment.Text.Trim();
        PlayerActionPressed?.Invoke(MentorHelpPlayerAction.Rate, up, comment.Length > 0 ? comment : null);
        _rateComment.Text = string.Empty;
    }

    private void Act(MentorHelpTicketView ticket, MentorHelpAction action, string? note = null, string? target = null)
    {
        ActionPressed?.Invoke(ticket.Id, action, note, target);
        _note.Text = string.Empty;
        _target.Text = string.Empty;
    }

    #endregion

    #region Controls

    private static BoxContainer Column()
    {
        return new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
    }

    private static BoxContainer Row()
    {
        return new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
    }

    private static ScrollContainer Scroll(Control content)
    {
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        scroll.AddChild(content);
        return scroll;
    }

    private static Button Button(string text, Action pressed)
    {
        var button = new Button { Text = Loc.GetString(text) };
        button.OnPressed += _ => pressed();
        return button;
    }

    #endregion
}
