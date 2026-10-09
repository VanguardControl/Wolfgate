using System.Linq;
using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.CombatConsole;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    private readonly Button[] _wfRecall = new Button[WFWeaponGroups.Count];
    private readonly Button[] _wfSave = new Button[WFWeaponGroups.Count];
    private WFCombatConsoleState? _wfCombat;
    private readonly Dictionary<NetEntity, WFGlassGauge> _wfAmmoGauges = new();
    private bool _wfConnected;
    private Control _wfCountermeasures = default!;
    private Button _wfAutomatic = default!;
    private Button _wfDispense = default!;
    private Label _wfFlareStatus = default!;
    private Label _wfSelection = default!;
    private Label _wfThreats = default!;
    private WFCountermeasureInstrument _wfDispenser = default!;
    private readonly WFThreatAnnunciator _wfAnnunciator = new();

    /// <summary>Forwards validated console settings through the existing bound interface.</summary>
    public event Action<BoundUserInterfaceMessage>? CombatMessage;

    private void WfInitializeCombatConsole()
    {
        MinSize = new Vector2(960, 600);
        FitWindow(this, new Vector2(1180, 780));
        SelectBallisticButton.Text = Loc.GetString("wf-console-ballistic");
        SelectEnergyButton.Text = Loc.GetString("wf-console-energy");
        SelectMissileButton.Text = Loc.GetString("wf-console-missile");

        var groups = new BoxContainer { HorizontalExpand = true, SeparationOverride = 6 };
        for (var i = 0; i < WFWeaponGroups.Count; i++)
        {
            var slot = i;
            var recall = _wfRecall[i] = new Button { HorizontalExpand = true };
            recall.AddStyleClass("WfGroup");
            Switch(recall);
            var save = _wfSave[i] = Button("wf-console-group-save");
            save.HorizontalExpand = false;
            save.MinWidth = 48;
            recall.Text = Loc.GetString("wf-console-group", ("slot", i + 1), ("count", 0));
            recall.Disabled = save.Disabled = true;
            save.ToolTip = Loc.GetString("wf-console-group-save-help");
            recall.OnPressed += _ => WfRecallGroup(slot);
            save.OnPressed += _ => CombatMessage?.Invoke(new WFSaveWeaponGroupMessage(slot,
                WeaponsList.Where(p => p.Value.Pressed && !(_wfCombat?.FlareLaunchers.Contains(p.Key) ?? false))
                    .Select(p => p.Key).ToList()));
            groups.AddChild(Row(recall, save));
        }

        _wfSelection = Label("wf-console-selection");
        _wfThreats = Label("wf-console-no-threats", Green);
        _wfAutomatic = Button("wf-console-auto-safe", true);
        _wfAutomatic.Visible = false;
        _wfAutomatic.Disabled = true;
        _wfAutomatic.ToolTip = Loc.GetString("wf-console-auto-help");
        _wfAutomatic.OnToggled += args => CombatMessage?.Invoke(new WFAutomaticFlaresMessage(args.Pressed));
        _wfDispense = Button("wf-console-dispense");
        _wfDispense.AddStyleClass("WfDispense");
        _wfDispense.Visible = false;
        _wfDispense.Disabled = true;
        _wfDispense.OnPressed += _ => CombatMessage?.Invoke(new WFDispenseFlaresMessage());
        _wfFlareStatus = Label("wf-console-no-launchers");

        var weapons = Column(Row(SelectAllButton, UnselectAllButton),
            Row(SelectBallisticButton, SelectEnergyButton, SelectMissileButton),
            ControllablesBox);
        weapons.VerticalExpand = true;
        var memory = new WFInstrumentPanel { HorizontalExpand = true };
        memory.AddChild(Row(Label("wf-console-groups-short", Accent), groups));
        var battery = Panel("wf-console-battery", weapons, true);
        battery.Name = "WfWeaponBattery";
        battery.SizeFlagsStretchRatio = 0.44f;
        battery.HorizontalAlignment = HAlignment.Stretch;
        battery.VerticalExpand = true;
        _wfDispenser = new WFCountermeasureInstrument { SetWidth = 48, MinHeight = 56, SetHeight = 64, VerticalAlignment = VAlignment.Center };
        var supply = new WFGlassGauge("wf-gauge-flares", () =>
        {
            var reading = WFGaugeReading.Number(_wfDispenser.Connected ? _wfCombat?.Ammunition : null,
                0, WFGaugeScale.Ceiling(_wfCombat?.Ammunition ?? 0, 30), "wf-gauge-unit-rounds",
                tint: _wfCombat is { Ammunition: 0, UnlimitedSupply: false } ? Red : Accent);
            return _wfDispenser.Connected && _wfCombat is { UnlimitedSupply: true }
                ? reading with { Value = reading.Maximum, Text = Loc.GetString("wf-gauge-autoloader") } : reading;
        }, true) { SetHeight = 56 };
        var threats = new WFGlassGauge("wf-gauge-threats", () => WFGaugeReading.Number(
            _wfConnected ? _wfCombat?.Threats : null, 0, WFGaugeScale.Ceiling(_wfCombat?.Threats ?? 0, 5),
            "wf-gauge-unit-count", tint: _wfCombat?.Threats > 0 ? Red : Green), true) { SetHeight = 56 };
        var cooldown = new WFGlassGauge("wf-gauge-cooldown", () => WFGaugeReading.Number(
            _wfDispenser.Connected ? _wfCombat?.Cooldown : null, 0, WFGaugeScale.Ceiling(_wfCombat?.Cooldown ?? 0, 15),
            "wf-gauge-unit-seconds", 1), true) { SetHeight = 56 };
        _wfThreats.Visible = _wfFlareStatus.Visible = false;
        _wfAutomatic.AddStyleClass("WfCompact");
        _wfDispense.AddStyleClass("WfCompact");
        var flareControls = Column(_wfAutomatic, _wfDispense);
        flareControls.MinWidth = 108;
        flareControls.MaxWidth = 108;
        flareControls.SeparationOverride = 4;
        var countermeasures = _wfCountermeasures = Panel("wf-console-countermeasures", Column(Row(_wfDispenser, supply, threats, cooldown, flareControls), _wfThreats, _wfFlareStatus));
        countermeasures.Name = "WfCountermeasurePanel";
        countermeasures.Visible = false;
        var scope = Column(Scope("wf-console-fire-scope", NavRadar, new Vector2(240, 120)), countermeasures);
        scope.HorizontalExpand = scope.VerticalExpand = true;
        scope.SizeFlagsStretchRatio = 0.56f;
        var instruments = Row(battery, scope);
        instruments.VerticalExpand = true;
        RefreshButton.HorizontalExpand = false;
        RefreshButton.MinWidth = 110;
        ServerStatus.HorizontalExpand = true;
        var header = Row(Label("wf-console-fire-title", Accent), new WFGlassReadout(ServerStatus), RefreshButton);
        var footer = Row(_wfSelection, IFFToggle, IFFDetailedToggle, DockToggle);
        var body = Column(header, memory, instruments, footer);
        body.Margin = new Thickness(10);
        body.HorizontalExpand = body.VerticalExpand = true;
        ContentsContainer.DisposeAllChildren();
        ContentsContainer.AddChild(body);
        Install(this);
        OnWeaponSelectionChanged += WfUpdateSelection;
        WfUpdateSelection();
    }

    private void WfRecallGroup(int slot)
    {
        if (_wfCombat == null)
            return;
        var selected = _wfCombat.Groups[slot].ToHashSet();
        foreach (var (uid, button) in WeaponsList)
            button.Pressed = selected.Contains(uid);
        OnWeaponSelectionChanged?.Invoke();
        UpdateAllWeaponButtonTexts();
    }

    private void WfUpdateSelection()
    {
        foreach (var (uid, button) in WeaponsList)
        {
            if (_wfCombat?.FlareLaunchers.Contains(uid) == true)
                button.Pressed = false;
        }
        var selected = WeaponsList.Where(p => p.Value.Pressed).Select(p => p.Key).ToHashSet();
        _wfSelection.Text = Loc.GetString("wf-console-selection-count", ("count", selected.Count));
        for (var i = 0; i < WFWeaponGroups.Count; i++)
            _wfRecall[i].Pressed = _wfCombat != null && _wfCombat.Groups[i].Count > 0 &&
                                   selected.SetEquals(_wfCombat.Groups[i]);
    }

    private void WfUpdateCombatConsole(FireControlConsoleBoundInterfaceState state)
    {
        if (_wfAnnunciator.Update(state.Combat.Threats, IsOpen && state.Connected))
            IoCManager.Resolve<IEntityManager>().System<WFConsoleAudio>().Warn();
        _wfCombat = state.Combat;
        _wfConnected = state.Connected;
        var combat = state.Combat;
        _weaponTypes.Clear();
        foreach (var (uid, type) in combat.WeaponTypes)
            _weaponTypes[uid] = type;
        SelectBallisticButton.Disabled = !_weaponTypes.Values.Contains(ShipGunType.Ballistic);
        SelectEnergyButton.Disabled = !_weaponTypes.Values.Contains(ShipGunType.Energy);
        SelectMissileButton.Disabled = !_weaponTypes.Values.Contains(ShipGunType.Missile);
        foreach (var (uid, button) in WeaponsList)
        {
            if (button.Stylesheet == null)
                Switch(button);
            button.ToolTip = button.Text;
            if (!_wfAmmoGauges.ContainsKey(uid))
            {
                var gauge = new WFGlassGauge("wf-gauge-supply", () => WfSupplyReading(uid), true)
                    { MinWidth = 0, SetHeight = 56, Margin = new Thickness(4, 38, 4, 0) };
                button.AddStyleClass("WfCompact");
                button.AddStyleClass("WfWeapon");
                Switch(button);
                button.Margin = new Thickness(0);
                button.RectClipContent = true;
                button.AddChild(gauge);
                _wfAmmoGauges[uid] = gauge;
            }
            button.Visible = !combat.FlareLaunchers.Contains(uid);
            if (!button.Visible)
                button.Pressed = false;
        }
        foreach (var removed in _wfAmmoGauges.Keys.Where(uid => !WeaponsList.ContainsKey(uid)).ToArray())
        {
            _wfAmmoGauges[removed].Dispose();
            _wfAmmoGauges.Remove(removed);
        }
        for (var i = 0; i < WFWeaponGroups.Count; i++)
        {
            _wfRecall[i].Text = Loc.GetString("wf-console-group", ("slot", i + 1), ("count", combat.Groups[i].Count));
            _wfRecall[i].Disabled = !state.Connected || combat.Groups[i].Count == 0;
            _wfSave[i].Disabled = !state.Connected;
        }
        _wfCountermeasures.Visible = _wfAutomatic.Visible = _wfDispense.Visible = combat.FlareLaunchers.Count > 0;
        _wfDispenser.Connected = state.Connected && combat.FlareLaunchers.Count > 0;
        _wfDispenser.Ammunition = combat.Ammunition;
        _wfDispenser.Unlimited = combat.UnlimitedSupply;
        _wfDispenser.Incoming = combat.Threats > 0;
        _wfDispenser.CoolingDown = combat.Cooldown > 0;
        _wfAutomatic.Pressed = combat.Automatic;
        _wfAutomatic.Disabled = !state.Connected || combat.FlareLaunchers.Count == 0;
        _wfAutomatic.Text = Loc.GetString(combat.Automatic ? "wf-console-auto-armed" : "wf-console-auto-safe");
        _wfDispense.Disabled = !state.Connected || combat.FlareLaunchers.Count == 0 ||
                              combat.Ammunition <= 0 && !combat.UnlimitedSupply || combat.Cooldown > 0;
        _wfFlareStatus.Text = combat.FlareLaunchers.Count == 0 ? Loc.GetString("wf-console-no-launchers") :
            Loc.GetString(combat.UnlimitedSupply ? "wf-console-flare-autoloader-status" : "wf-console-flare-status", ("count", combat.FlareLaunchers.Count),
                ("ammo", combat.Ammunition), ("seconds", (int) Math.Ceiling(combat.Cooldown)));
        _wfDispenser.ToolTip = _wfFlareStatus.Text;
        _wfThreats.Text = combat.Threats == 0 ? Loc.GetString("wf-console-no-threats") :
            Loc.GetString("wf-console-threats", ("count", combat.Threats));
        _wfThreats.FontColorOverride = combat.Threats == 0 ? Green : Red;
        ServerStatus.FontColorOverride = state.Connected ? Green : Red;
        WfUpdateSelection();
        UpdateAllWeaponButtonTexts();
    }
}
