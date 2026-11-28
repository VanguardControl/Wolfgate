using Content.Shared._WG.Mentors;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Mentors;

/// <summary>
/// The leaderboard and My stats for a period: in the mentor window for mentors, and in the staff Mentors tab, where
/// the server adds escalations and fallbacks.
/// </summary>
public sealed class MentorBoardControl : BoxContainer
{
    [Dependency] private IEntitySystemManager _systems = default!;

    private readonly ButtonGroup _periods = new();
    private readonly Label _mine = new();
    private readonly Label _fallbacks = new();
    private readonly GridContainer _grid = new() { HorizontalExpand = true };
    private MentorBoardPeriod _period = MentorBoardPeriod.Week;
    private WGClientMentorPageSystem? _system;

    public MentorBoardControl()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;

        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        AddPeriod(row, MentorBoardPeriod.Week, "wg-mentor-board-week");
        AddPeriod(row, MentorBoardPeriod.Month, "wg-mentor-board-month");
        AddPeriod(row, MentorBoardPeriod.All, "wg-mentor-board-all");
        AddChild(row);

        AddChild(new Label { Text = Loc.GetString("wg-mentor-board-mine"), StyleClasses = { "LabelHeading" } });
        AddChild(_mine);
        AddChild(_fallbacks);

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_grid);
        AddChild(scroll);
    }

    private void AddPeriod(BoxContainer row, MentorBoardPeriod period, string text)
    {
        var button = new Button { Text = Loc.GetString(text), ToggleMode = true, Group = _periods, Pressed = period == _period };
        button.OnPressed += _ =>
        {
            _period = period;
            Request();
        };
        row.AddChild(button);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (_systems.TryGetEntitySystem(out _system))
            _system.BoardReceived += OnBoard;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (_system != null)
            _system.BoardReceived -= OnBoard;
        _system = null;
    }

    /// <summary>Asks the server for the selected period.</summary>
    public void Request()
    {
        _system?.RequestBoard(_period);
    }

    private void OnBoard(WGMentorBoardEvent ev)
    {
        if (ev.Period != _period)
            return;

        _mine.Text = ev.Mine != null
            ? Loc.GetString("wg-mentor-board-rank", ("rank", ev.MyRank), ("total", ev.Rows.Count))
            : "";

        _fallbacks.Visible = ev.Staff;
        _fallbacks.Text = Loc.GetString("wg-mentor-board-fallbacks", ("count", ev.Fallbacks));

        _grid.RemoveAllChildren();
        _grid.Columns = ev.Staff ? 9 : 8;
        Header("wg-mentor-board-col-mentor");
        Header("wg-mentor-board-col-resolved");
        Header("wg-mentor-board-col-helpful");
        Header("wg-mentor-board-col-rating");
        Header("wg-mentor-board-col-first-answer");
        Header("wg-mentor-board-col-assists");
        Header("wg-mentor-board-col-watches");
        Header("wg-mentor-board-col-critters");
        if (ev.Staff)
            Header("wg-mentor-board-col-escalations");

        if (ev.Rows.Count == 0)
        {
            _mine.Text = Loc.GetString("wg-mentor-board-empty");
            return;
        }

        foreach (var row in ev.Rows)
        {
            Cell(row.Name);
            Cell(row.Resolved.ToString());
            Cell(row.Helpful.ToString());
            Cell(row.Rated >= MentorBoardRules.MinRatingsShown
                ? $"{100 * row.Helpful / row.Rated}%"
                : Loc.GetString("wg-mentor-board-few-ratings"));
            Cell(row.MedianFirstAnswer is { } seconds ? Duration(seconds) : "-");
            Cell(row.Assists.ToString());
            Cell(row.Watches.ToString());
            Cell(row.CritterVisits.ToString());
            if (ev.Staff)
                Cell(row.Escalations.ToString());
        }
    }

    private static string Duration(float seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    private void Header(string text)
    {
        _grid.AddChild(new Label { Text = Loc.GetString(text), FontColorOverride = Color.Gray, Margin = new Thickness(0, 0, 8, 0) });
    }

    private void Cell(string text)
    {
        _grid.AddChild(new Label { Text = text, Margin = new Thickness(0, 0, 8, 0) });
    }
}
