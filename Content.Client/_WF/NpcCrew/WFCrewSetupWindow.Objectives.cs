using System.Globalization;
using System.Linq;
using Content.Shared._WF.NpcCrew;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.NpcCrew;

public sealed partial class WFCrewSetupWindow
{
    private readonly OptionButton _activeCrew = new();
    private readonly OptionButton _objectiveKind = new();
    private readonly OptionButton _objectiveTarget = new();
    private readonly LineEdit _duration = new() { Text = "0" };
    private readonly LineEdit _objectiveRange = new() { Text = "300" };
    private readonly LineEdit _objectiveX = new() { Text = "0" };
    private readonly LineEdit _objectiveY = new() { Text = "0" };
    private readonly BoxContainer _queueRows = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private List<WFCrewSetupCrew> _liveCrews = new();
    private List<WFCrewObjective> _draft = new();
    private int? _editing;
    private NetEntity? _selectedObjectiveTarget;
    private bool _queueDirty;
    private BoxContainer _objectivePositionLine = default!;
    private BoxContainer _objectiveTimingLine = default!;
    private readonly Label _objectiveHelp = new();

    private void BuildObjectives(BoxContainer body)
    {
        SelectOnClick(_activeCrew);
        SelectOnClick(_objectiveKind);
        SelectOnClick(_objectiveTarget);
        _objectiveTarget.OnItemSelected += args => _selectedObjectiveTarget = args.Id >= 0 && args.Id < _grids.Count ? _grids[args.Id].Id : null;
        foreach (var kind in Enum.GetValues<WFCrewObjectiveKind>())
            _objectiveKind.AddItem(Loc.GetString($"wf-crew-objective-{kind.ToString().ToLowerInvariant()}"), (int) kind);
        _activeCrew.OnItemSelected += args =>
        {
            if (args.Id >= _liveCrews.Count)
                return;
            var crew = _liveCrews[args.Id];
            var index = _grids.FindIndex(grid => grid.Id == crew.Grid);
            if (index < 0)
                return;
            _grid.SelectId(index);
            _group.Text = crew.Group;
            var settings = crew.Settings;
            _callsign.Text = settings.Callsign;
            _company.TrySelectId(Math.Max(0, _companies.IndexOf(settings.Company)));
            _faction.TrySelectId(Math.Max(0, _factions.IndexOf(settings.Faction)));
            _local.TrySelectId(Math.Max(0, _channels.IndexOf(settings.LocalChannel)));
            _alert.TrySelectId(Math.Max(0, _channels.IndexOf(settings.AlertChannel)));
            _boardingRule.SelectId((int) settings.BoardingResponse);
            _dockingRule.SelectId((int) settings.DockingResponse);
            _heave.Pressed = settings.HeaveTo;
            _draft = crew.Objectives.ToList();
            _queueDirty = false;
            _editing = null;
            _rows.Clear();
            _roster.RemoveAllChildren();
            RefreshTargets(null);
            FillObjectiveTargets();
            RenderQueue();
            _status.Text = crew.Status;
        };
        _grid.OnItemSelected += _ => { _draft.Clear(); RenderQueue(); FillObjectiveTargets(); };
        body.AddChild(Line("objective", _objectiveKind, _objectiveTarget));
        _objectiveTimingLine = Line("objective-duration", _duration, new Label { Text = Loc.GetString("wf-crew-setup-range") }, _objectiveRange);
        _objectivePositionLine = Line("destination", _objectiveX, _objectiveY);
        body.AddChild(_objectiveTimingLine);
        body.AddChild(_objectivePositionLine);
        body.AddChild(_objectiveHelp);
        _objectiveKind.OnItemSelected += _ => UpdateObjectiveFields();
        UpdateObjectiveFields();
        body.AddChild(Line("queue-actions", Button("queue-add", AddObjective), Button("queue-append", () => SendQueue(WFCrewSetupAction.AppendObjective))));
        body.AddChild(new ScrollContainer { MinHeight = 90, MaxHeight = 150, Children = { _queueRows } });
        body.AddChild(Line("queue-control", Button("queue-save", () => SendQueue(WFCrewSetupAction.Objectives)),
            Button("queue-pause", () => SendQueue(WFCrewSetupAction.Pause)),
            Button("queue-resume", () => SendQueue(WFCrewSetupAction.Resume)), Button("queue-skip", () => SendQueue(WFCrewSetupAction.Skip))));
        body.AddChild(new Label { Text = Loc.GetString("wf-crew-setup-queue-help") });
    }

    private void UpdateObjectiveFields()
    {
        var kind = (WFCrewObjectiveKind) _objectiveKind.SelectedId;
        _objectivePositionLine.Visible = kind == WFCrewObjectiveKind.GoTo;
        _objectiveTarget.Visible = kind is not (WFCrewObjectiveKind.Hold or WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Undock or WFCrewObjectiveKind.Repair);
        _objectiveTimingLine.Visible = kind is WFCrewObjectiveKind.Hold or WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Loiter
            or WFCrewObjectiveKind.Follow or WFCrewObjectiveKind.Attack or WFCrewObjectiveKind.Retreat or WFCrewObjectiveKind.Escort;
        _objectiveHelp.Text = Loc.GetString(kind switch
        {
            WFCrewObjectiveKind.Repair => "wf-crew-setup-repair-help",
            WFCrewObjectiveKind.Resupply => "wf-crew-setup-resupply-help",
            WFCrewObjectiveKind.Salvage => "wf-crew-setup-salvage-help",
            WFCrewObjectiveKind.Attack => "wf-crew-setup-attack-help",
            WFCrewObjectiveKind.Escort => "wf-crew-setup-escort-help",
            _ => "wf-crew-setup-objective-help",
        });
    }

    private void FillObjectiveTargets()
    {
        var previous = _selectedObjectiveTarget;
        _objectiveTarget.Clear();
        _objectiveTarget.AddItem(Loc.GetString("wf-crew-setup-select-target"), -1);
        _objectiveTarget.SelectId(-1);
        for (var index = 0; index < _grids.Count; index++)
        {
            if (index == _grid.SelectedId)
                continue;
            _objectiveTarget.AddItem($"{_grids[index].Name} ({_grids[index].Id})", index);
            if (_grids[index].Id == previous)
                _objectiveTarget.SelectId(index);
        }
    }

    private WFCrewObjective ReadObjective() => new()
    {
        Kind = (WFCrewObjectiveKind) _objectiveKind.SelectedId,
        Target = _objectiveTarget.SelectedId >= 0 && _objectiveTarget.SelectedId < _grids.Count ? _grids[_objectiveTarget.SelectedId].Id : null,
        Position = new System.Numerics.Vector2(Number(_objectiveX), Number(_objectiveY)),
        Range = Number(_objectiveRange), Duration = Number(_duration),
    };

    private void AddObjective()
    {
        _queueDirty = true;
        if (_editing is { } index && index < _draft.Count)
            _draft[index] = ReadObjective();
        else
            _draft.Add(ReadObjective());
        _editing = null;
        RenderQueue();
    }

    private void SendQueue(WFCrewSetupAction action)
    {
        if (_grids.Count == 0)
            return;
        _system.Send(new WFCrewSetupRequest
        {
            Action = action, Grid = _grids[_grid.SelectedId].Id,
            Mission = new WFCrewMission { Group = _group.Text },
            Objectives = action == WFCrewSetupAction.AppendObjective ? new List<WFCrewObjective> { ReadObjective() } : _draft.ToList(),
        });
    }

    private void RenderQueue()
    {
        _queueRows.RemoveAllChildren();
        for (var index = 0; index < _draft.Count; index++)
        {
            var at = index;
            var item = _draft[index];
            var name = Loc.GetString($"wf-crew-objective-{item.Kind.ToString().ToLowerInvariant()}");
            var target = _grids.FirstOrDefault(grid => grid.Id == item.Target)?.Name ?? item.Position.ToString();
            var line = new BoxContainer();
            line.AddChild(new Label { Text = $"{index + 1}. {name}: {target}", HorizontalExpand = true });
            line.AddChild(Button("queue-edit", () =>
            {
                _editing = at;
                _queueDirty = true;
                _objectiveKind.SelectId((int) item.Kind);
                UpdateObjectiveFields();
                _objectiveTarget.TrySelectId(_grids.FindIndex(grid => grid.Id == item.Target));
                _selectedObjectiveTarget = item.Target;
                _duration.Text = item.Duration.ToString(CultureInfo.InvariantCulture);
                _objectiveRange.Text = item.Range.ToString(CultureInfo.InvariantCulture);
                _objectiveX.Text = item.Position.X.ToString(CultureInfo.InvariantCulture);
                _objectiveY.Text = item.Position.Y.ToString(CultureInfo.InvariantCulture);
            }));
            line.AddChild(Button("queue-up", () =>
            {
                if (at > 0) { (_draft[at - 1], _draft[at]) = (_draft[at], _draft[at - 1]); _queueDirty = true; _editing = null; RenderQueue(); }
            }));
            line.AddChild(Button("remove", () => { _draft.RemoveAt(at); _queueDirty = true; _editing = null; RenderQueue(); }));
            _queueRows.AddChild(line);
        }
    }

    private void ReceiveCrews(List<WFCrewSetupCrew> crews)
    {
        var previous = _activeCrew.SelectedId >= 0 && _activeCrew.SelectedId < _liveCrews.Count ? _liveCrews[_activeCrew.SelectedId] : null;
        _liveCrews = crews;
        _activeCrew.Clear();
        for (var index = 0; index < crews.Count; index++)
        {
            var crew = crews[index];
            var ship = _grids.FirstOrDefault(grid => grid.Id == crew.Grid)?.Name ?? crew.Grid.ToString();
            _activeCrew.AddItem($"{ship} / {crew.Group} ({crew.Alive}/{crew.Members}) — {crew.Status}", index);
            if (previous?.Grid == crew.Grid && previous.Group == crew.Group)
                _activeCrew.SelectId(index);
        }
        FillObjectiveTargets();
        if (!_queueDirty && _grids.Count > 0)
        {
            var current = crews.FirstOrDefault(crew => crew.Grid == _grids[_grid.SelectedId].Id && crew.Group == _group.Text);
            if (current != null)
            {
                _draft = current.Objectives.ToList();
                RenderQueue();
            }
        }
    }
}
