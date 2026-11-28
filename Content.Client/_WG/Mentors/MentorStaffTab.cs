using System.Numerics;
using Content.Shared._WG.Mentors;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client._WG.Mentors;

/// <summary>
/// The Mentors tab in the admin menu: the roster with grant, revoke, tier, restore and notes; pending applications;
/// stored tickets with search and transcripts; and the leaderboard with the staff columns.
/// </summary>
public sealed class MentorStaffTab : Control
{
    [Dependency] private IEntitySystemManager _systems = default!;

    private readonly TabContainer _tabs = new() { VerticalExpand = true, HorizontalExpand = true };
    private readonly Label _result = new() { FontColorOverride = Color.Khaki };

    private readonly LineEdit _player = new() { HorizontalExpand = true };
    private readonly OptionButton _tier = new();
    private readonly LineEdit _text = new() { HorizontalExpand = true };
    private readonly BoxContainer _roster = Column();

    private readonly MentorApplicationsControl _applications = new() { VerticalExpand = true };

    private readonly LineEdit _search = new() { HorizontalExpand = true };
    private readonly BoxContainer _tickets = Column();
    private readonly BoxContainer _transcript = Column();

    private readonly MentorBoardControl _board = new() { VerticalExpand = true };

    private WGClientMentorPageSystem? _system;

    public MentorStaffTab()
    {
        IoCManager.InjectDependencies(this);
        MinSize = new Vector2(620, 420);

        var root = Column();
        root.AddChild(_result);
        root.AddChild(_tabs);
        AddChild(root);

        _tabs.AddChild(BuildRoster());
        _tabs.AddChild(_applications);
        _tabs.AddChild(BuildTickets());
        _tabs.AddChild(_board);
        _tabs.SetTabTitle(0, Loc.GetString("wg-mentor-tab-roster"));
        _tabs.SetTabTitle(1, Loc.GetString("wg-mentor-tab-applications"));
        _tabs.SetTabTitle(2, Loc.GetString("wg-mentor-tab-tickets"));
        _tabs.SetTabTitle(3, Loc.GetString("wg-mentor-tab-stats"));
        _tabs.OnTabChanged += _ => Request();
    }

    private Control BuildRoster()
    {
        var page = Column();
        var row = Row();
        _player.PlaceHolder = Loc.GetString("wg-mentor-tab-player");
        row.AddChild(_player);
        foreach (var tier in new[] { MentorTier.Trainee, MentorTier.Mentor, MentorTier.Senior })
        {
            _tier.AddItem(MentoringWindow.TierName(tier), (int) tier);
        }

        _tier.OnItemSelected += args => _tier.SelectId(args.Id);
        row.AddChild(_tier);
        page.AddChild(row);

        _text.PlaceHolder = Loc.GetString("wg-mentor-tab-text");
        page.AddChild(_text);

        var actions = Row();
        actions.AddChild(Action("wg-mentor-tab-grant", MentorStaffAction.Grant));
        actions.AddChild(Action("wg-mentor-tab-tier", MentorStaffAction.Tier));
        actions.AddChild(Action("wg-mentor-tab-revoke", MentorStaffAction.Revoke));
        actions.AddChild(Action("wg-mentor-tab-restore", MentorStaffAction.Restore));
        actions.AddChild(Action("wg-mentor-tab-notes", MentorStaffAction.Notes));
        page.AddChild(actions);

        page.AddChild(Scroll(_roster));
        return page;
    }

    private Button Action(string text, MentorStaffAction action)
    {
        var button = new Button { Text = Loc.GetString(text) };
        button.OnPressed += _ => _system?.StaffAction(action, _player.Text, (MentorTier) _tier.SelectedId, _text.Text);
        return button;
    }

    private Control BuildTickets()
    {
        var page = Column();
        var row = Row();
        _search.PlaceHolder = Loc.GetString("wg-mentor-tab-search");
        _search.OnTextEntered += _ => Request();
        row.AddChild(_search);
        var go = new Button { Text = Loc.GetString("wg-mentor-tab-search-button") };
        go.OnPressed += _ => Request();
        row.AddChild(go);
        page.AddChild(row);

        var split = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, VerticalExpand = true, SeparationOverride = 6 };
        var list = Scroll(_tickets);
        list.MinWidth = 260;
        split.AddChild(list);
        var transcript = Scroll(_transcript);
        transcript.HorizontalExpand = true;
        split.AddChild(transcript);
        page.AddChild(split);
        return page;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (!_systems.TryGetEntitySystem(out _system))
            return;

        _system.RosterReceived += OnRoster;
        _system.TicketsReceived += OnTickets;
        _system.TranscriptReceived += OnTranscript;
        _system.StaffResult += OnResult;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (_system != null)
        {
            _system.RosterReceived -= OnRoster;
            _system.TicketsReceived -= OnTickets;
            _system.TranscriptReceived -= OnTranscript;
            _system.StaffResult -= OnResult;
        }

        _system = null;
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (!newVisible)
            return;

        GrowWindow();
        Request();
    }

    /// <summary>Asks the server for the part of the tab that is showing.</summary>
    private void Request()
    {
        switch (_tabs.CurrentTab)
        {
            case 0:
                _system?.RequestStaff(MentorStaffView.Roster);
                break;
            case 1:
                _applications.Request();
                break;
            case 2:
                _system?.RequestStaff(MentorStaffView.Tickets, _search.Text);
                break;
            default:
                _board.Request();
                break;
        }
    }

    private void OnResult(string message)
    {
        _result.Text = message;
    }

    private void OnRoster(WGMentorStaffRosterEvent ev)
    {
        _roster.RemoveAllChildren();
        foreach (var row in ev.Rows)
        {
            var box = Column();
            var state = Loc.GetString($"wg-mentor-state-{row.State}");
            var duty = row.Connected
                ? Loc.GetString(row.OnDuty ? "wg-mentor-tab-on-duty" : "wg-mentor-tab-online")
                : Loc.GetString("wg-mentor-tab-offline");

            var name = new Button
            {
                Text = Loc.GetString("wg-mentor-tab-row",
                    ("player", row.Name), ("tier", MentoringWindow.TierName(row.Tier)), ("state", state), ("duty", duty),
                    ("resolved", row.Resolved), ("helpful", row.Helpful), ("rated", row.Rated),
                    ("active", row.LastActiveAt.ToString("yyyy-MM-dd"))),
                ToggleMode = true,
                HorizontalExpand = true,
            };

            var details = Column();
            details.Visible = false;
            details.Margin = new Thickness(12, 0, 0, 4);
            details.AddChild(new Label { Text = Loc.GetString("wg-mentor-tab-history"), FontColorOverride = Color.Gray });
            foreach (var line in row.History)
            {
                details.AddChild(new Label { Text = line });
            }

            if (row.Notes.Length > 0)
            {
                details.AddChild(new Label { Text = Loc.GetString("wg-mentor-tab-notes"), FontColorOverride = Color.Gray });
                details.AddChild(new Label { Text = row.Notes });
            }

            name.OnToggled += args =>
            {
                details.Visible = args.Pressed;
                _player.Text = row.Name;
                _tier.SelectId((int) row.Tier);
            };

            box.AddChild(name);
            box.AddChild(details);
            _roster.AddChild(box);
        }
    }

    private void OnTickets(WGMentorStaffTicketsEvent ev)
    {
        _tickets.RemoveAllChildren();
        foreach (var ticket in ev.Rows)
        {
            var rating = ticket.Rating switch
            {
                > 0 => " +",
                < 0 => " -",
                _ => "",
            };

            var button = new Button
            {
                Text = Loc.GetString("wg-mentor-tab-ticket-row",
                    ("id", ticket.Id), ("date", ticket.OpenedAt.ToString("yyyy-MM-dd HH:mm")),
                    ("player", ticket.Player), ("character", ticket.Character),
                    ("mentor", ticket.Mentor.Length > 0 ? ticket.Mentor : "-"),
                    ("reason", ticket.CloseReason.Length > 0 ? ticket.CloseReason : "-")) + rating,
                HorizontalExpand = true,
                ClipText = true,
            };

            var id = ticket.Id;
            button.OnPressed += _ => _system?.RequestTranscript(id);
            _tickets.AddChild(button);
        }

        if (ev.Rows.Count == 0)
            _tickets.AddChild(new Label { Text = Loc.GetString("wg-mhelp-queue-empty") });
    }

    private void OnTranscript(WGMentorTranscriptEvent ev)
    {
        _transcript.RemoveAllChildren();
        _transcript.AddChild(new Label { Text = $"#{ev.Id}", StyleClasses = { "LabelHeading" } });
        foreach (var line in ev.Lines)
        {
            var label = new RichTextLabel { HorizontalExpand = true };
            label.SetMessage(Robust.Shared.Utility.FormattedMessage.FromUnformatted(line));
            _transcript.AddChild(label);
        }
    }

    /// <summary>Makes the admin menu big enough for this tab, which it doesn't measure until shown.</summary>
    private void GrowWindow()
    {
        var parent = Parent;
        while (parent != null && parent is not BaseWindow)
        {
            parent = parent.Parent;
        }

        if (parent is not BaseWindow window)
            return;

        window.Measure(Vector2Helpers.Infinity);
        var wanted = Vector2.Max(window.Size, window.DesiredSize);
        if (wanted != window.Size)
            window.SetSize = wanted;
    }

    private static ScrollContainer Scroll(Control content)
    {
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(content);
        return scroll;
    }

    private static BoxContainer Column()
    {
        return new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, HorizontalExpand = true };
    }

    private static BoxContainer Row()
    {
        return new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
    }
}
