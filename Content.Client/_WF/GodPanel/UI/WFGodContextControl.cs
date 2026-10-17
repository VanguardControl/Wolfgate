using System.Linq;
using Content.Client.Administration.Managers;
using Content.Shared._WF.GodPanel;
using Content.Shared._WF.ShipPa;
using Content.Shared.Administration;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.GodPanel.UI;

/// <summary>
/// The strip under the map: a line about the selection and the buttons that act on it. A player gets the player
/// buttons and a grid the ship buttons.
/// </summary>
public sealed partial class WFGodContextControl : PanelContainer
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IClientConsoleHost _console = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly WFGodPanelSystem _system;
    private readonly Label _summary = new() { ClipText = true, StyleClasses = { "LabelKeyText" } };
    private readonly WrapContainer _playerBar = new();
    private readonly WrapContainer _gridBar = new();
    private readonly List<(Control Control, AdminFlags Flag, Func<bool>? Enabled)> _gated = new();
    private readonly List<string> _codeIds = new();
    private readonly List<string> _parts = new();

    private readonly CheckBox _announcePower = new() { Text = Loc.GetString("wf-godpanel-ctx-with-announcement") };
    private readonly LineEdit _announceText = new() { MinWidth = 220, PlaceHolder = Loc.GetString("wf-godpanel-ctx-announce-placeholder") };
    private readonly LineEdit _soundUrl = new() { MinWidth = 180, PlaceHolder = Loc.GetString("wf-godpanel-ctx-sound-placeholder") };
    private readonly OptionButton _code = new() { MinWidth = 120 };

    private WFGodPlayer? _player;
    private WFGodGrid? _grid;

    /// <summary>The Watch button was pressed for the selection.</summary>
    public event Action<NetEntity>? WatchRequested;

    public WFGodContextControl()
    {
        IoCManager.InjectDependencies(this);
        _system = _entities.System<WFGodPanelSystem>();
        PanelOverride = new StyleBoxFlat { BackgroundColor = WFGodPanelText.PanelColor };
        HorizontalExpand = true;

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6, 4) };
        box.AddChild(_summary);
        box.AddChild(_playerBar);
        box.AddChild(_gridBar);
        AddChild(box);

        BuildPlayerBar();
        BuildGridBar();
        Show(null, null);
    }

    private void BuildPlayerBar()
    {
        Add(_playerBar, "wf-godpanel-ctx-watch", AdminFlags.Admin, () => WatchRequested?.Invoke(PlayerTarget()), PlayerReady);
        Add(_playerBar, "wf-godpanel-ctx-follow", AdminFlags.Admin, () => PlayerAct(WFGodAction.Follow), PlayerReady);
        Add(_playerBar, "wf-godpanel-ctx-goto", AdminFlags.Admin, () => PlayerAct(WFGodAction.GoTo), PlayerReady);
        Add(_playerBar, "wf-godpanel-ctx-bring", AdminFlags.Admin, () => PlayerAct(WFGodAction.Bring), PlayerReady);
        Add(_playerBar, "wf-godpanel-ctx-second-chance", AdminFlags.Admin, () => PlayerAct(WFGodAction.SecondChance), PlayerReady);
        Add(_playerBar, "wf-godpanel-ctx-offer", AdminFlags.Admin, () => PlayerAct(WFGodAction.OfferToGhosts),
            () => PlayerReady() && _player!.Status != WFGodPlayerStatus.Alive, "wf-godpanel-ctx-offer-tooltip");
        Add(_playerBar, "wf-godpanel-ctx-rejuvenate", AdminFlags.Admin, () => PlayerAct(WFGodAction.Rejuvenate), PlayerReady);
        Add(_playerBar, "wf-godpanel-ctx-outfit", AdminFlags.Spawn,
            () => _console.ExecuteCommand($"spawnoutfitui {PlayerTarget().Id}"), PlayerReady);
        Add(_playerBar, "wf-godpanel-ctx-player-panel", AdminFlags.Admin,
            () => _console.ExecuteCommand($"playerpanel {_player!.Username}"), () => _player != null && _player.Username.Length > 0);
        Add(_playerBar, "wf-godpanel-ctx-camera", AdminFlags.Admin,
            () => _console.ExecuteCommand($"camera {PlayerTarget()}"), PlayerReady);
    }

    private void BuildGridBar()
    {
        Add(_gridBar, "wf-godpanel-ctx-watch", AdminFlags.Admin, () => WatchRequested?.Invoke(_grid!.Entity), GridReady);
        Add(_gridBar, "wf-godpanel-ctx-goto", AdminFlags.Admin, () => GridAct(WFGodAction.GoTo), GridReady);
        Add(_gridBar, "wf-godpanel-ctx-power-on", AdminFlags.Admin,
            () => GridAct(WFGodAction.PowerOn, flag: _announcePower.Pressed), GridReady);
        Add(_gridBar, "wf-godpanel-ctx-power-off", AdminFlags.Admin,
            () => GridAct(WFGodAction.PowerOff, flag: _announcePower.Pressed), GridReady);
        _gridBar.AddChild(_announcePower);
        Add(_gridBar, "wf-godpanel-ctx-gq-on", AdminFlags.Fun, () => GridAct(WFGodAction.GeneralQuartersOn), GridReady);
        Add(_gridBar, "wf-godpanel-ctx-gq-off", AdminFlags.Fun, () => GridAct(WFGodAction.GeneralQuartersOff), GridReady);
        Add(_gridBar, "wf-godpanel-ctx-sos-on", AdminFlags.Fun, () => GridAct(WFGodAction.DistressOn), GridReady);
        Add(_gridBar, "wf-godpanel-ctx-sos-off", AdminFlags.Fun, () => GridAct(WFGodAction.DistressOff), GridReady);
        Add(_gridBar, "wf-godpanel-ctx-hail", AdminFlags.Fun, () => GridAct(WFGodAction.HailAsGod), GridReady);
        Add(_gridBar, "wf-godpanel-ctx-crew", AdminFlags.Spawn, () => _console.ExecuteCommand("wf_crew"), GridReady);
        Add(_gridBar, "wf-godpanel-ctx-end-encounter", AdminFlags.Admin,
            () => GridAct(WFGodAction.EndEncounter, _grid!.Encounter ?? _grid.Entity), () => GridReady() && _grid!.Encounter != null);

        _gridBar.AddChild(_announceText);
        Add(_gridBar, "wf-godpanel-ctx-announce", AdminFlags.Fun,
            () => GridAct(WFGodAction.Announce, text: _announceText.Text.Trim()),
            () => GridReady() && _announceText.Text.Trim().Length > 0);
        _announceText.OnTextChanged += _ => Refresh();
        _announceText.OnTextEntered += _ =>
        {
            if (_grid != null && _announceText.Text.Trim().Length > 0 && _admin.HasFlag(AdminFlags.Fun))
                GridAct(WFGodAction.Announce, text: _announceText.Text.Trim());
        };

        _code.AddItem(Loc.GetString("wf-godpanel-ctx-code"), 0);
        foreach (var prototype in _prototypes.EnumeratePrototypes<ShipAlertCodePrototype>()
                     .Where(prototype => prototype.Selectable)
                     .OrderBy(prototype => prototype.Order))
        {
            _codeIds.Add(prototype.ID);
            _code.AddItem(Loc.GetString(prototype.Name), _codeIds.Count);
        }

        _code.OnItemSelected += args =>
        {
            _code.SelectId(0);
            if (args.Id > 0 && args.Id <= _codeIds.Count && _grid != null)
                GridAct(WFGodAction.SetCode, text: _codeIds[args.Id - 1]);
        };
        _gridBar.AddChild(_code);
        _gated.Add((_code, AdminFlags.Fun, GridReady));

        _gridBar.AddChild(_soundUrl);
        _soundUrl.OnTextChanged += _ => Refresh();
        Add(_gridBar, "wf-godpanel-ctx-pa-play", AdminFlags.Fun,
            () => GridAct(WFGodAction.PlayPaSound, text: _soundUrl.Text.Trim()),
            () => GridReady() && _soundUrl.Text.Trim().Length > 0);
        Add(_gridBar, "wf-godpanel-ctx-pa-stop", AdminFlags.Fun, () => GridAct(WFGodAction.StopPaSound), GridReady);
    }

    private void Add(WrapContainer bar, string loc, AdminFlags flag, Action action, Func<bool>? enabled = null, string? tooltip = null)
    {
        var button = WFGodPanelText.SmallButton(Loc.GetString(loc), tooltip == null ? null : Loc.GetString(tooltip));
        button.Margin = new Thickness(0, 2, 4, 2);
        button.OnPressed += _ => action();
        bar.AddChild(button);
        _gated.Add((button, flag, enabled));
    }

    private bool PlayerReady() => _player is { Entity: not null };

    private bool GridReady() => _grid != null;

    private NetEntity PlayerTarget() => _player!.Entity!.Value;

    private void PlayerAct(WFGodAction action)
    {
        if (_player is { Entity: { } entity })
            _system.Act(action, entity);
    }

    private void GridAct(WFGodAction action, NetEntity? target = null, string? text = null, bool flag = false)
    {
        if (_grid is { } grid)
            _system.Act(action, target ?? grid.Entity, text, flag);
    }

    /// <summary>Shows the selection: a player, a grid or neither. The records are the latest from the state.</summary>
    public void Show(WFGodPlayer? player, WFGodGrid? grid)
    {
        _player = player;
        _grid = grid;

        _playerBar.Visible = player != null;
        _gridBar.Visible = grid != null;
        _summary.Text = player != null ? PlayerSummary(player) : grid != null ? GridSummary(grid) : Loc.GetString("wf-godpanel-ctx-none");
        Refresh();
    }

    /// <summary>Enables the buttons the local admin has the flag for and the selection allows.</summary>
    private void Refresh()
    {
        foreach (var (control, flag, enabled) in _gated)
        {
            var allowed = _admin.HasFlag(flag) && (enabled == null || enabled());
            if (control is BaseButton button)
                button.Disabled = !allowed;
        }

        _announcePower.Disabled = !_admin.HasFlag(AdminFlags.Admin);
        _announceText.Editable = _admin.HasFlag(AdminFlags.Fun);
        _soundUrl.Editable = _admin.HasFlag(AdminFlags.Fun);
    }

    private string PlayerSummary(WFGodPlayer player)
    {
        _parts.Clear();
        _parts.Add(player.Character.Length > 0 && player.Username.Length > 0
            ? Loc.GetString("wf-godpanel-player-title", ("character", player.Character), ("username", player.Username))
            : player.Character.Length > 0 ? player.Character : player.Username);
        if (player.Job.Length > 0)
            _parts.Add(player.Job);

        _parts.Add(player.Status is WFGodPlayerStatus.Alive or WFGodPlayerStatus.Critical
            ? Loc.GetString("wf-godpanel-ctx-health", ("status", WFGodPanelText.StatusName(player.Status)), ("health", WFGodPanelText.Percent(player.Health)))
            : WFGodPanelText.StatusName(player.Status));

        if (player.GridName.Length > 0)
            _parts.Add(Loc.GetString("wf-godpanel-ctx-on-grid", ("grid", player.GridName)));
        if (player.Antag)
            _parts.Add(player.AntagRole.Length > 0 ? player.AntagRole : Loc.GetString("wf-godpanel-tag-antag"));

        return Loc.GetString("wf-godpanel-ctx-selected", ("what", string.Join(" | ", _parts)));
    }

    private string GridSummary(WFGodGrid grid)
    {
        _parts.Clear();
        _parts.Add(grid.Name);
        _parts.Add(WFGodPanelText.KindName(grid.Kind));
        if (grid.Owner.Length > 0)
            _parts.Add(Loc.GetString("wf-godpanel-ctx-owner", ("owner", grid.Owner)));

        _parts.Add(Loc.GetString("wf-godpanel-grid-people", ("players", grid.Players), ("crew", grid.Crew)));
        _parts.Add(grid.HasPower
            ? Loc.GetString("wf-godpanel-ctx-power", ("percent", WFGodPanelText.Percent(grid.PowerFraction)), ("on", grid.BreakersOn), ("total", grid.Breakers))
            : Loc.GetString("wf-godpanel-grid-no-power"));
        if (grid.Code.Length > 0)
            _parts.Add(Loc.GetString("wf-godpanel-grid-code", ("code", grid.Code)));
        if (grid.GeneralQuarters)
            _parts.Add(Loc.GetString("wf-godpanel-tag-gq"));
        if (grid.Distress)
            _parts.Add(Loc.GetString("wf-godpanel-tag-sos"));
        if (grid.Encounter != null)
            _parts.Add(Loc.GetString("wf-godpanel-ctx-encounter", ("name", grid.EncounterName), ("state", grid.EncounterState)));
        if (grid.DockedTo.Length > 0)
            _parts.Add(Loc.GetString("wf-godpanel-ctx-docked", ("to", grid.DockedTo)));
        if (grid.Sanctuary)
            _parts.Add(Loc.GetString("wf-godpanel-tag-sanctuary"));

        return Loc.GetString("wf-godpanel-ctx-selected", ("what", string.Join(" | ", _parts)));
    }
}
