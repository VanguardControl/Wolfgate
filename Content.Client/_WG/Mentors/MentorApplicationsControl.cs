using Content.Shared._WG.Mentors;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WG.Mentors;

/// <summary>
/// Pending mentor applications with their answers and record, to approve or deny with a reason and a reapply wait.
/// In the staff Mentors tab and, for Senior Mentors, in the mentor window (D3).
/// </summary>
public sealed class MentorApplicationsControl : BoxContainer
{
    [Dependency] private IEntitySystemManager _systems = default!;

    private readonly BoxContainer _list = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
    private readonly Label _result = new() { FontColorOverride = Color.Khaki };
    private WGClientMentorPageSystem? _system;

    public MentorApplicationsControl()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;

        AddChild(_result);
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_list);
        AddChild(scroll);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (!_systems.TryGetEntitySystem(out _system))
            return;

        _system.ApplicationsReceived += OnApplications;
        _system.StaffResult += OnResult;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (_system != null)
        {
            _system.ApplicationsReceived -= OnApplications;
            _system.StaffResult -= OnResult;
        }

        _system = null;
    }

    public void Request()
    {
        _system?.RequestStaff(MentorStaffView.Applications);
    }

    private void OnResult(string message)
    {
        _result.Text = message;
    }

    private void OnApplications(WGMentorStaffApplicationsEvent ev)
    {
        _list.RemoveAllChildren();
        if (ev.Rows.Count == 0)
        {
            _list.AddChild(new Label { Text = Loc.GetString("wg-mentor-review-none") });
            return;
        }

        foreach (var application in ev.Rows)
        {
            _list.AddChild(Build(application));
        }
    }

    private Control Build(MentorApplicationRow application)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        box.AddChild(new Label
        {
            Text = Loc.GetString("wg-mentor-review-header",
                ("id", application.Id), ("player", application.Name), ("hours", application.Hours),
                ("date", application.CreatedAt.ToString("yyyy-MM-dd"))),
            StyleClasses = { "LabelHeading" },
        });

        foreach (var id in MentorForm.Questions)
        {
            if (!application.Answers.TryGetValue(id, out var answer))
                continue;

            box.AddChild(Text($"[color=gray]{FormattedMessage.EscapeText(Loc.GetString($"form-{id}"))}[/color]\n{FormattedMessage.EscapeText(answer)}"));
        }

        foreach (var concern in application.Record)
        {
            box.AddChild(new Label { Text = concern, FontColorOverride = Color.Khaki });
        }

        var reason = new LineEdit { HorizontalExpand = true, PlaceHolder = Loc.GetString("wg-mentor-tab-reason") };
        var days = new LineEdit { MinWidth = 60, PlaceHolder = Loc.GetString("wg-mentor-tab-reapply") };
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        var approve = new Button { Text = Loc.GetString("wg-mentor-tab-approve") };
        approve.OnPressed += _ => _system?.Review(application.Id, true, "", 0);
        var deny = new Button { Text = Loc.GetString("wg-mentor-tab-deny") };
        deny.OnPressed += _ => _system?.Review(application.Id, false, reason.Text,
            int.TryParse(days.Text, out var wait) ? wait : 0);

        row.AddChild(approve);
        row.AddChild(reason);
        row.AddChild(days);
        row.AddChild(deny);
        box.AddChild(row);
        box.AddChild(new HSeparator());
        return box;
    }

    private static RichTextLabel Text(string markup)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromMarkupPermissive(markup));
        return label;
    }
}
