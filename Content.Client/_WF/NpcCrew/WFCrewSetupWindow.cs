using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.NpcCrew;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Radio;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.NpcCrew;

/// <summary>Plans and edits ship crews, then spawns them or updates their mission.</summary>
[UsedImplicitly]
public sealed partial class WFCrewSetupWindow : DefaultWindow
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    private readonly WFCrewSetupClientSystem _system;
    private readonly OptionButton _grid = new();
    private readonly OptionButton _target = new();
    private readonly OptionButton _vessel = new();
    private readonly OptionButton _order = new();
    private readonly OptionButton _company = new();
    private readonly OptionButton _faction = new();
    private readonly OptionButton _local = new();
    private readonly OptionButton _alert = new();
    private readonly OptionButton _boardingRule = new();
    private readonly OptionButton _dockingRule = new();
    private readonly LineEdit _count = new() { Text = "2" };
    private readonly LineEdit _group = new() { Text = "crew" };
    private readonly LineEdit _callsign = new();
    private readonly LineEdit _x = new() { Text = "0" };
    private readonly LineEdit _y = new() { Text = "0" };
    private readonly LineEdit _range = new() { Text = "60" };
    private readonly CheckBox _captain = new();
    private readonly CheckBox _heave = new() { Pressed = true };
    private readonly Label _status = new() { ClipText = true };
    private readonly BoxContainer _targetLine;
    private readonly BoxContainer _destinationLine;
    private readonly BoxContainer _roster = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly List<Row> _rows = new();
    private List<WFCrewSetupGrid> _grids = new();
    private readonly List<string> _roles;
    private readonly List<string> _loadouts;
    private readonly List<string> _vessels;
    private readonly List<string> _companies;
    private readonly List<string> _factions;
    private readonly List<string> _channels;
    private NetEntity? _pendingGrid;
    private bool _planAfterList;

    /// <summary>Selects a crew requested by an admin verb.</summary>
    public void SelectCrew(NetEntity grid, string group)
    {
        _pendingGrid = grid;
        _group.Text = group;
        _planAfterList = true;
    }

    public WFCrewSetupWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("wf-crew-setup-title");
        MinSize = new Vector2(860, 640);
        _system = _entities.System<WFCrewSetupClientSystem>();
        _system.Received += Receive;
        OnClose += () => _system.Received -= Receive;
        _roles = _prototypes.EnumeratePrototypes<WFCrewRolePrototype>().OrderBy(role => role.Order).Select(role => role.ID).ToList();
        _loadouts = new List<string> { string.Empty };
        _loadouts.AddRange(_prototypes.EnumeratePrototypes<StartingGearPrototype>().Select(gear => gear.ID).Order());
        _vessels = _prototypes.EnumeratePrototypes<VesselPrototype>().Where(vessel => !vessel.Abstract).Select(vessel => vessel.ID).Order().ToList();
        _companies = new List<string> { string.Empty };
        _companies.AddRange(_prototypes.EnumeratePrototypes<CompanyPrototype>().Select(company => company.ID).Order());
        _factions = _prototypes.EnumeratePrototypes<NpcFactionPrototype>().Select(faction => faction.ID).Order().ToList();
        _channels = _prototypes.EnumeratePrototypes<RadioChannelPrototype>().Select(channel => channel.ID).Order().ToList();
        Fill(_vessel, _vessels, string.Empty);
        Fill(_company, _companies, string.Empty);
        Fill(_faction, _factions, "WFCrew");
        Fill(_local, _channels, "Traffic");
        Fill(_alert, _channels, "Common");
        foreach (var order in Enum.GetValues<WFPilotOrder>())
            _order.AddItem(Loc.GetString($"wf-crew-setup-order-{order.ToString().ToLowerInvariant()}"), (int) order);
        SelectOnClick(_order);
        SelectOnClick(_grid);
        SelectOnClick(_target);
        _grid.OnItemSelected += _ => { _rows.Clear(); _roster.RemoveAllChildren(); RefreshTargets(_target.SelectedId >= 0 && _target.SelectedId < _grids.Count ? _grids[_target.SelectedId].Id : null); };
        _order.OnItemSelected += _ => UpdateOrderFields();

        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        Contents.AddChild(body);
        body.AddChild(Line("ship", _grid, Button("refresh", () => Send(WFCrewSetupAction.List))));
        body.AddChild(Line("active-crews", _activeCrew));
        body.AddChild(Line("group", _group, new Label { Text = Loc.GetString("wf-crew-setup-callsign") }, _callsign));
        var tabs = new TabContainer { VerticalExpand = true };
        body.AddChild(tabs);
        BoxContainer Page(string key)
        {
            var page = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10 };
            tabs.AddChild(new ScrollContainer { Children = { page } });
            tabs.SetTabTitle(tabs.ChildCount - 1, Loc.GetString($"wf-crew-setup-tab-{key}"));
            return page;
        }
        var orders = Page("objectives");
        BuildObjectives(orders);
        var setup = Page("crew");
        var rules = Page("rules");
        var immediate = Page("manual");
        setup.AddChild(Line("vessel", _vessel, Button("spawn-vessel", () => Send(WFCrewSetupAction.SpawnVessel))));
        _captain.Text = Loc.GetString("wf-crew-setup-captain");
        _heave.Text = Loc.GetString("wf-crew-setup-heave");
        setup.AddChild(Line("deckhands", _count, _captain, Button("plan", () => Send(WFCrewSetupAction.Plan)),
            Button("add", () => AddRow(new WFCrewSetupPost()))));
        setup.AddChild(new Label { Text = Loc.GetString("wf-crew-setup-roster-help") });
        setup.AddChild(new ScrollContainer { VerticalExpand = true, MinHeight = 170, Children = { _roster } });
        rules.AddChild(Line("company", _company, new Label { Text = Loc.GetString("wf-crew-setup-faction") }, _faction));
        rules.AddChild(Line("channels", _local, _alert, _heave));
        foreach (var button in new[] { _boardingRule, _dockingRule })
        {
            foreach (var response in Enum.GetValues<WFCrewSecurityResponse>())
                button.AddItem(Loc.GetString($"wf-crew-setup-response-{response.ToString().ToLowerInvariant()}"), (int) response);
            button.SelectId((int) WFCrewSecurityResponse.Hostile);
            SelectOnClick(button);
        }
        rules.AddChild(Line("boarding-rule", _boardingRule));
        rules.AddChild(Line("docking-rule", _dockingRule));
        rules.AddChild(new Label { Text = Loc.GetString("wf-crew-setup-security-help") });
        rules.AddChild(Button("apply-rules", () => Send(WFCrewSetupAction.Rules)));
        immediate.AddChild(Line("orders", _order));
        _targetLine = Line("target-grid", _target, Button("refresh", () => Send(WFCrewSetupAction.List)));
        immediate.AddChild(_targetLine);
        _destinationLine = Line("destination", _x, _y);
        immediate.AddChild(_destinationLine);
        immediate.AddChild(Line("range", _range));
        UpdateOrderFields();
        setup.AddChild(Line("actions", Button("preview", () => Send(WFCrewSetupAction.Preview)),
            Button("spawn", () => Send(WFCrewSetupAction.Spawn)), Button("clear", () => Send(WFCrewSetupAction.Clear))));
        immediate.AddChild(Button("apply-orders", () => Send(WFCrewSetupAction.Orders)));
        body.AddChild(_status);
        Send(WFCrewSetupAction.List);
    }

    private static void SelectOnClick(OptionButton button) => button.OnItemSelected += args => button.SelectId(args.Id);

    private static void Fill(OptionButton button, List<string> choices, string selected)
    {
        for (var index = 0; index < choices.Count; index++)
            button.AddItem(choices[index].Length > 0 ? choices[index] : Loc.GetString("wf-crew-setup-default"), index);
        button.TrySelectId(Math.Max(0, choices.IndexOf(selected)));
        SelectOnClick(button);
    }

    private static Button Button(string key, Action pressed)
    {
        var button = new Button { Text = Loc.GetString($"wf-crew-setup-{key}") };
        button.OnPressed += _ => pressed();
        return button;
    }

    private static BoxContainer Line(string key, params Control[] controls)
    {
        var line = new BoxContainer { SeparationOverride = 6 };
        line.AddChild(new Label { Text = Loc.GetString($"wf-crew-setup-{key}"), MinWidth = 110 });
        foreach (var control in controls)
        {
            control.HorizontalExpand = true;
            line.AddChild(control);
        }
        return line;
    }

    private void AddRow(WFCrewSetupPost post)
    {
        var row = new Row();
        Fill(row.Role, _roles, post.Role);
        for (var index = 0; index < _roles.Count; index++)
            row.Role.SetItemText(index, Loc.GetString(_prototypes.Index<WFCrewRolePrototype>(_roles[index]).Title));
        Fill(row.Loadout, _loadouts, post.Loadout);
        row.X.Text = post.Position.X.ToString(CultureInfo.InvariantCulture);
        row.Y.Text = post.Position.Y.ToString(CultureInfo.InvariantCulture);
        row.Engagement.AddItem(Loc.GetString("wf-crew-setup-default"), 0);
        row.Engagement.AddItem(Loc.GetString("wf-crew-setup-onsight"), 1);
        row.Engagement.AddItem(Loc.GetString("wf-crew-setup-attacked"), 2);
        row.Engagement.SelectId(post.Engagement is { } engagement ? (int) engagement + 1 : 0);
        SelectOnClick(row.Engagement);
        foreach (var control in new Control[] { row.Role, row.Loadout, row.Engagement, row.X, row.Y })
        {
            control.HorizontalExpand = true;
            row.Box.AddChild(control);
        }
        row.X.MinWidth = row.Y.MinWidth = 55;
        row.Box.AddChild(Button("teleport", () => Send(WFCrewSetupAction.Teleport, row)));
        row.Box.AddChild(Button("remove", () => { _rows.Remove(row); _roster.RemoveChild(row.Box); }));
        _rows.Add(row);
        _roster.AddChild(row.Box);
    }

    private static float Number(LineEdit edit) => float.TryParse(edit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : float.NaN;

    private void UpdateOrderFields()
    {
        var order = (WFPilotOrder) _order.SelectedId;
        _targetLine.Visible = order is WFPilotOrder.Dock or WFPilotOrder.Follow;
        _destinationLine.Visible = order is WFPilotOrder.GoTo or WFPilotOrder.Loiter;
    }

    private void RefreshTargets(NetEntity? selected)
    {
        _target.Clear();
        _target.AddItem(Loc.GetString("wf-crew-setup-select-target"), -1);
        _target.SelectId(-1);
        for (var index = 0; index < _grids.Count; index++)
        {
            if (index == _grid.SelectedId)
                continue;
            _target.AddItem($"{_grids[index].Name} ({_grids[index].Id})", index);
            if (_grids[index].Id == selected)
                _target.SelectId(index);
        }
    }

    private WFCrewSetupPost Read(Row row) => new()
    {
        Role = _roles[row.Role.SelectedId], Loadout = _loadouts[row.Loadout.SelectedId],
        Engagement = row.Engagement.SelectedId == 0 ? null : (WFCrewEngagement) (row.Engagement.SelectedId - 1),
        Position = new Vector2(Number(row.X), Number(row.Y)),
    };

    private void Send(WFCrewSetupAction action, Row? row = null)
    {
        _system.Send(new WFCrewSetupRequest
        {
            Action = action, Grid = _grids.Count > 0 ? _grids[_grid.SelectedId].Id : null,
            Deckhands = int.TryParse(_count.Text, out var count) ? count : 2, Captain = _captain.Pressed,
            Vessel = _vessels.Count > 0 ? _vessels[_vessel.SelectedId] : string.Empty,
            Posts = row != null ? new List<WFCrewSetupPost> { Read(row) } : _rows.Select(Read).ToList(),
            Mission = new WFCrewMission
            {
                Group = _group.Text, Callsign = _callsign.Text, Company = _companies[_company.SelectedId],
                Faction = _factions[_faction.SelectedId], LocalChannel = _channels[_local.SelectedId],
                AlertChannel = _channels[_alert.SelectedId], HeaveTo = _heave.Pressed,
                BoardingResponse = (WFCrewSecurityResponse) _boardingRule.SelectedId,
                DockingResponse = (WFCrewSecurityResponse) _dockingRule.SelectedId,
                Order = (WFPilotOrder) _order.SelectedId, Destination = new Vector2(Number(_x), Number(_y)),
                Range = Number(_range), Target = _target.SelectedId >= 0 && _target.SelectedId < _grids.Count ? _grids[_target.SelectedId].Id : null,
            },
        });
    }

    private void Receive(WFCrewSetupResponse response)
    {
        if (response.Action != WFCrewSetupAction.List)
            _status.Text = response.Message;
        if (response.Action == WFCrewSetupAction.List)
        {
            var selected = _pendingGrid ?? (_grids.Count > 0 ? _grids[_grid.SelectedId].Id : (NetEntity?) null);
            var target = _target.SelectedId >= 0 && _target.SelectedId < _grids.Count ? _grids[_target.SelectedId].Id : (NetEntity?) null;
            _grids = response.Grids;
            _grid.Clear();
            _target.Clear();
            for (var index = 0; index < _grids.Count; index++)
            {
                _grid.AddItem($"{_grids[index].Name} ({_grids[index].Id})", index);
            }
            _grid.TrySelectId(Math.Max(0, _grids.FindIndex(grid => grid.Id == selected)));
            RefreshTargets(target);
            _pendingGrid = null;
            if (_planAfterList)
            {
                _planAfterList = false;
                Send(WFCrewSetupAction.Plan);
            }
        }
        else if (response.Action == WFCrewSetupAction.Plan)
        {
            if (_grids.Count == 0 || _grids[_grid.SelectedId].Id != response.Grid)
                return;
            _rows.Clear();
            _roster.RemoveAllChildren();
            foreach (var post in response.Posts)
                AddRow(post);
        }
        else if (response.Action == WFCrewSetupAction.SpawnVessel && response.Grid != null)
        {
            _pendingGrid = response.Grid;
            Send(WFCrewSetupAction.List);
        }
        ReceiveCrews(response.Crews);
        if (response.Message.Length == 0 && response.Action is WFCrewSetupAction.Objectives or WFCrewSetupAction.AppendObjective
            or WFCrewSetupAction.Pause or WFCrewSetupAction.Resume or WFCrewSetupAction.Skip)
        {
            var crew = response.Crews.FirstOrDefault(row => row.Grid == response.Grid && row.Group == _group.Text);
            if (crew != null)
            {
                _draft = crew.Objectives.ToList();
                _queueDirty = false;
                _editing = null;
                RenderQueue();
                _status.Text = crew.Status;
            }
        }
    }

    private sealed class Row
    {
        public readonly BoxContainer Box = new() { SeparationOverride = 4 };
        public readonly OptionButton Role = new();
        public readonly OptionButton Loadout = new();
        public readonly OptionButton Engagement = new();
        public readonly LineEdit X = new();
        public readonly LineEdit Y = new();
    }
}
