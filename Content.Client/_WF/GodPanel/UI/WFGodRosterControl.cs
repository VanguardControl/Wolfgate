using System.Linq;
using Content.Shared._WF.GodPanel;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Network;

namespace Content.Client._WF.GodPanel.UI;

/// <summary>
/// The panel's left side: a searchable list of players, a list of ships, and the round and encounter overview.
/// </summary>
public sealed partial class WFGodRosterControl : BoxContainer
{
    [Dependency] private IEntityManager _entities = default!;

    private enum Tab : byte
    {
        Players,
        Ships,
        Ops,
    }

    private readonly Dictionary<NetUserId, WFGodRow> _players = new();
    private readonly Dictionary<NetEntity, WFGodRow> _grids = new();
    private readonly Dictionary<NetEntity, WFGodRow> _encounters = new();

    private readonly List<Control> _desired = new();
    private readonly List<(string Text, Color Color, bool Italic)> _tags = new();
    private readonly List<string> _parts = new();

    private readonly LineEdit _search = new();
    private readonly BoxContainer _list = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly Label _empty = new() { StyleClasses = { "LabelSubText" }, Margin = new Thickness(6) };
    private readonly ScrollContainer _listScroll = new() { VerticalExpand = true, HScrollEnabled = false };
    private readonly ScrollContainer _opsScroll = new() { VerticalExpand = true, HScrollEnabled = false, Visible = false };
    private readonly BoxContainer _ops = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly List<Label> _opsLines = new();
    private readonly BoxContainer _encounterList = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly Label _encountersHeading = new() { StyleClasses = { "LabelHeading" }, Margin = new Thickness(6, 8, 0, 2) };

    private WFGodPanelState? _state;
    private Tab _tab;
    private NetEntity? _selected;
    private bool _selectedIsGrid;

    /// <summary>A row was picked; the arguments are the entity and whether it is a grid.</summary>
    public event Action<NetEntity, bool>? Selected;

    /// <summary>A row was double-clicked: the panel should open a feed on it.</summary>
    public event Action<NetEntity, bool>? Watch;

    public WFGodRosterControl()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        MinWidth = 220;

        var group = new ButtonGroup(false);
        var playersTab = MakeTab("wf-godpanel-tab-players", Tab.Players, group, "OpenRight");
        var shipsTab = MakeTab("wf-godpanel-tab-ships", Tab.Ships, group, "OpenBoth");
        var opsTab = MakeTab("wf-godpanel-tab-ops", Tab.Ops, group, "OpenLeft");
        playersTab.Pressed = true;

        var tabs = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        tabs.AddChild(playersTab);
        tabs.AddChild(shipsTab);
        tabs.AddChild(opsTab);
        AddChild(tabs);

        _search.PlaceHolder = Loc.GetString("wf-godpanel-search");
        _search.OnTextChanged += _ => Refresh();
        AddChild(_search);

        _listScroll.AddChild(_list);
        AddChild(_listScroll);

        _encountersHeading.Text = Loc.GetString("wf-godpanel-ops-encounters");
        _ops.AddChild(_encountersHeading);
        _ops.AddChild(_encounterList);
        _opsScroll.AddChild(_ops);
        AddChild(_opsScroll);
    }

    private Button MakeTab(string loc, Tab tab, ButtonGroup group, string edge)
    {
        var button = new Button
        {
            Text = Loc.GetString(loc),
            ToggleMode = true,
            Group = group,
            HorizontalExpand = true,
            StyleClasses = { edge },
        };
        button.OnPressed += _ =>
        {
            _tab = tab;
            Refresh();
        };
        return button;
    }

    /// <summary>Takes a new state and refreshes the visible tab.</summary>
    public void Update(WFGodPanelState state)
    {
        _state = state;
        Refresh();
    }

    /// <summary>Highlights the row for the panel's selection.</summary>
    public void SetSelection(NetEntity? entity, bool isGrid)
    {
        _selected = entity;
        _selectedIsGrid = isGrid;
        MarkSelected();
    }

    private void MarkSelected()
    {
        foreach (var row in _players.Values)
        {
            row.Selected = !_selectedIsGrid && _selected != null && row.Entity == _selected;
        }

        foreach (var row in _grids.Values)
        {
            row.Selected = _selectedIsGrid && _selected != null && row.Entity == _selected;
        }
    }

    private void Refresh()
    {
        _listScroll.Visible = _tab != Tab.Ops;
        _opsScroll.Visible = _tab == Tab.Ops;
        _search.Visible = _tab != Tab.Ops;

        if (_state is not { } state)
            return;

        switch (_tab)
        {
            case Tab.Players:
                RefreshPlayers(state);
                break;
            case Tab.Ships:
                RefreshShips(state);
                break;
            default:
                RefreshOps(state);
                break;
        }
    }

    private static bool Matches(string search, params string[] fields)
    {
        if (search.Length == 0)
            return true;

        foreach (var field in fields)
        {
            if (field.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int Rank(WFGodPlayerStatus status)
    {
        return status switch
        {
            WFGodPlayerStatus.Alive or WFGodPlayerStatus.Critical => 0,
            WFGodPlayerStatus.Dead => 1,
            WFGodPlayerStatus.Ghost => 2,
            WFGodPlayerStatus.Lobby => 3,
            _ => 4,
        };
    }

    private void RefreshPlayers(WFGodPanelState state)
    {
        var search = _search.Text.Trim();
        var shown = new List<WFGodPlayer>();
        var live = new HashSet<NetUserId>();
        foreach (var player in state.Players)
        {
            live.Add(player.User);
            if (Matches(search, player.Character, player.Username, player.Job, player.GridName))
                shown.Add(player);
        }

        shown.Sort((a, b) =>
        {
            var rank = Rank(a.Status).CompareTo(Rank(b.Status));
            if (rank != 0)
                return rank;

            return string.Compare(a.Character.Length > 0 ? a.Character : a.Username,
                b.Character.Length > 0 ? b.Character : b.Username, StringComparison.CurrentCultureIgnoreCase);
        });

        // Drop the rows of players that left.
        foreach (var key in _players.Keys.Where(key => !live.Contains(key)).ToList())
        {
            _players.Remove(key);
        }

        _desired.Clear();
        foreach (var player in shown)
        {
            if (!_players.TryGetValue(player.User, out var row))
            {
                row = new WFGodRow();
                row.Clicked += OnRowClicked;
                row.DoubleClicked += OnRowDoubleClicked;
                _players[player.User] = row;
            }

            FillPlayer(row, player);
            _desired.Add(row);
        }

        Apply();
        MarkSelected();
    }

    private void FillPlayer(WFGodRow row, WFGodPlayer player)
    {
        row.Entity = player.Entity;
        row.IsGrid = false;
        row.Dot.Color = WFGodPanelText.StatusColor(player.Status);

        row.Title.Text = player.Character.Length > 0 && player.Username.Length > 0
            ? Loc.GetString("wf-godpanel-player-title", ("character", player.Character), ("username", player.Username))
            : player.Character.Length > 0 ? player.Character : player.Username;

        _parts.Clear();
        if (player.Job.Length > 0)
            _parts.Add(player.Job);
        if (player.GridName.Length > 0)
            _parts.Add(player.GridName);
        if (player.Status != WFGodPlayerStatus.Alive)
            _parts.Add(WFGodPanelText.StatusName(player.Status));
        row.Sub.Text = string.Join(", ", _parts);

        row.SetHealth(player.Status is WFGodPlayerStatus.Alive or WFGodPlayerStatus.Critical ? player.Health : null);

        _tags.Clear();
        if (player.Antag)
            _tags.Add((player.AntagRole.Length > 0 ? player.AntagRole : Loc.GetString("wf-godpanel-tag-antag"), WFGodPanelText.Red, false));
        if (player.Admin)
            _tags.Add((Loc.GetString("wf-godpanel-tag-admin"), WFGodPanelText.Gold, false));
        if (player.Offered)
            _tags.Add((Loc.GetString("wf-godpanel-tag-offered"), WFGodPanelText.Dim, true));
        row.SetTags(_tags);
    }

    private void RefreshShips(WFGodPanelState state)
    {
        var search = _search.Text.Trim();
        var shown = new List<WFGodGrid>();
        var live = new HashSet<NetEntity>();
        foreach (var grid in state.Grids)
        {
            live.Add(grid.Entity);
            if (Matches(search, grid.Name, grid.Owner, grid.EncounterName, grid.Code, grid.DockedTo))
                shown.Add(grid);
        }

        shown.Sort((a, b) =>
        {
            var kind = a.Kind.CompareTo(b.Kind);
            return kind != 0 ? kind : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        foreach (var key in _grids.Keys.Where(key => !live.Contains(key)).ToList())
        {
            _grids.Remove(key);
        }

        _desired.Clear();
        foreach (var grid in shown)
        {
            if (!_grids.TryGetValue(grid.Entity, out var row))
            {
                row = new WFGodRow();
                row.Clicked += OnRowClicked;
                row.DoubleClicked += OnRowDoubleClicked;
                _grids[grid.Entity] = row;
            }

            FillGrid(row, grid);
            _desired.Add(row);
        }

        Apply();
        MarkSelected();
    }

    private void FillGrid(WFGodRow row, WFGodGrid grid)
    {
        row.Entity = grid.Entity;
        row.IsGrid = true;
        row.Dot.Color = WFGodPanelText.KindColor(grid.Kind);
        row.Title.Text = grid.Name;
        row.SetHealth(null);

        _parts.Clear();
        if (grid.Owner.Length > 0)
            _parts.Add(grid.Owner);
        _parts.Add(Loc.GetString("wf-godpanel-grid-people", ("players", grid.Players), ("crew", grid.Crew)));
        _parts.Add(grid.HasPower
            ? Loc.GetString("wf-godpanel-grid-power", ("percent", WFGodPanelText.Percent(grid.PowerFraction)))
            : Loc.GetString("wf-godpanel-grid-no-power"));
        if (grid.Code.Length > 0)
            _parts.Add(Loc.GetString("wf-godpanel-grid-code", ("code", grid.Code)));
        row.Sub.Text = string.Join(" | ", _parts);

        _tags.Clear();
        _tags.Add((WFGodPanelText.KindName(grid.Kind), WFGodPanelText.KindColor(grid.Kind), false));
        if (grid.GeneralQuarters)
            _tags.Add((Loc.GetString("wf-godpanel-tag-gq"), WFGodPanelText.Red, false));
        if (grid.Distress)
            _tags.Add((Loc.GetString("wf-godpanel-tag-sos"), WFGodPanelText.Orange, false));
        if (grid.Encounter != null)
            _tags.Add((Loc.GetString("wf-godpanel-tag-encounter"), Color.FromHex("#BA68C8"), false));
        if (grid.Sanctuary)
            _tags.Add((Loc.GetString("wf-godpanel-tag-sanctuary"), WFGodPanelText.Blue, false));
        row.SetTags(_tags);
    }

    private void RefreshOps(WFGodPanelState state)
    {
        var round = state.Round;
        var lines = new List<string>
        {
            Loc.GetString("wf-godpanel-ops-round", ("time", WFGodPanelText.Clock(round.RoundTime))),
            Loc.GetString("wf-godpanel-ops-war", ("level", Loc.GetString(round.WarHot ? "wf-godpanel-war-hot" : "wf-godpanel-war-cold"))),
            Loc.GetString("wf-godpanel-ops-reavers", ("tier", round.ReaverTier), ("name", round.ReaverTierName), ("cells", round.ReaverCells)),
            Loc.GetString("wf-godpanel-ops-reavers-state", ("paused", round.ReaversPaused ? "yes" : "no")),
            Loc.GetString("wf-godpanel-ops-encounters-state", ("running", round.EncountersRunning), ("paused", round.EncountersPaused ? "yes" : "no")),
            round.SoundTitle is { } title
                ? Loc.GetString("wf-godpanel-ops-sound", ("title", title), ("requester", round.SoundRequester ?? string.Empty))
                : Loc.GetString("wf-godpanel-ops-sound-none"),
            Loc.GetString("wf-godpanel-ops-gms", ("count", round.Gms)),
        };

        while (_opsLines.Count < lines.Count)
        {
            var label = new Label { Margin = new Thickness(6, 1) };
            _ops.AddChild(label);
            label.SetPositionInParent(_opsLines.Count);
            _opsLines.Add(label);
        }

        for (var i = 0; i < lines.Count; i++)
        {
            _opsLines[i].Text = lines[i];
        }

        // One row per encounter, listing its ships.
        var groups = new Dictionary<NetEntity, (string Name, string State, List<string> Ships)>();
        foreach (var grid in state.Grids)
        {
            if (grid.Encounter is not { } encounter)
                continue;

            if (!groups.TryGetValue(encounter, out var group))
            {
                group = (grid.EncounterName, grid.EncounterState, new List<string>());
                groups[encounter] = group;
            }

            group.Ships.Add(grid.Name);
        }

        foreach (var key in _encounters.Keys.Where(key => !groups.ContainsKey(key)).ToList())
        {
            _encounters.Remove(key);
        }

        var rows = new List<Control>();
        foreach (var (encounter, group) in groups.OrderBy(pair => pair.Value.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!_encounters.TryGetValue(encounter, out var row))
            {
                var target = encounter;
                row = new WFGodRow { Entity = target };
                var end = WFGodPanelText.SmallButton(Loc.GetString("wf-godpanel-ops-end"));
                end.OnPressed += _ => _entities.System<WFGodPanelSystem>().Act(WFGodAction.EndEncounter, target);
                row.Side.AddChild(end);
                row.Dot.Color = Color.FromHex("#BA68C8");
                row.SetHealth(null);
                _encounters[encounter] = row;
            }

            row.Title.Text = group.Name;
            row.Sub.Text = Loc.GetString("wf-godpanel-ops-encounter-line",
                ("state", group.State), ("ships", string.Join(", ", group.Ships)));
            rows.Add(row);
        }

        _encountersHeading.Text = Loc.GetString("wf-godpanel-ops-encounters-heading", ("count", groups.Count));
        Fill(_encounterList, rows);
    }

    /// <summary>Puts the wanted rows in the list, or a note when there are none.</summary>
    private void Apply()
    {
        if (_desired.Count == 0)
        {
            _empty.Text = Loc.GetString("wf-godpanel-roster-empty");
            _desired.Add(_empty);
        }

        Fill(_list, _desired);
    }

    /// <summary>Rebuilds a list's children only when they differ from the wanted order.</summary>
    private static void Fill(BoxContainer list, List<Control> wanted)
    {
        var same = list.ChildCount == wanted.Count;
        for (var i = 0; same && i < wanted.Count; i++)
        {
            same = list.GetChild(i) == wanted[i];
        }

        if (same)
            return;

        list.RemoveAllChildren();
        foreach (var control in wanted)
        {
            list.AddChild(control);
        }
    }

    private void OnRowClicked(WFGodRow row)
    {
        if (row.Entity is { } entity)
            Selected?.Invoke(entity, row.IsGrid);
    }

    private void OnRowDoubleClicked(WFGodRow row)
    {
        if (row.Entity is { } entity)
            Watch?.Invoke(entity, row.IsGrid);
    }
    private System.Numerics.Vector2 _lastMeasured;

    /// <summary>
    /// The engine only re-arranges a control whose final rect changed, so a header measured before the styles gave
    /// it a height stayed at zero while the list filled the space. A changed measure asks for a fresh arrange.
    /// </summary>
    protected override System.Numerics.Vector2 MeasureOverride(System.Numerics.Vector2 availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        if (size != _lastMeasured)
        {
            _lastMeasured = size;
            InvalidateArrange();
        }

        return size;
    }
}
