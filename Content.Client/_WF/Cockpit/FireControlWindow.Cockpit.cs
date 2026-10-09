using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.CombatConsole;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    private Button? _wfCockpitStore;
    private Control? _wfCockpitCountermeasures;

    /// <summary>Borrows the live battery and switches into a compact cockpit instrument bank.</summary>
    internal Control WfBuildCockpitGunnery(WFCockpitLease lease)
    {
        Button Borrow(Button button, string? key = null)
        {
            lease.Take(button);
            button.AddStyleClass("WfCompact");
            if (key != null)
            {
                button.ToolTip = button.Text;
                button.Text = Loc.GetString(key);
            }
            Switch(button);
            return button;
        }

        var select = Row(Borrow(SelectAllButton, "wf-cockpit-guns-all"),
            Borrow(UnselectAllButton, "wf-cockpit-guns-none"),
            Borrow(SelectBallisticButton, "wf-cockpit-guns-ballistic"),
            Borrow(SelectEnergyButton, "wf-cockpit-guns-energy"),
            Borrow(SelectMissileButton, "wf-cockpit-guns-missile"));
        select.SeparationOverride = 2;
        _wfCockpitStore = Button("wf-cockpit-guns-store", true);
        _wfCockpitStore.Name = "CockpitGroupStore";
        _wfCockpitStore.ToolTip = Loc.GetString("wf-cockpit-guns-store-help");
        _wfCockpitStore.AddStyleClass("WfCompact");
        Switch(_wfCockpitStore);
        _wfCockpitStore.OnToggled += _ => WfShowCockpitGroupMode();
        var memory = Row();
        memory.SeparationOverride = 2;
        for (var i = 0; i < WFWeaponGroups.Count; i++)
        {
            var recall = Borrow(_wfRecall[i]);
            var save = Borrow(_wfSave[i]);
            recall.Name = $"CockpitGroupRecall{i}";
            save.Name = $"CockpitGroupSave{i}";
            save.Text = Loc.GetString("wf-cockpit-guns-slot", ("slot", i + 1));
            save.OnPressed += _ =>
            {
                _wfCockpitStore.Pressed = false;
                WfShowCockpitGroupMode();
            };
            var slot = new Control { HorizontalExpand = true, SetHeight = 32 };
            slot.AddChild(recall);
            slot.AddChild(save);
            memory.AddChild(slot);
        }
        memory.AddChild(_wfCockpitStore);
        WfShowCockpitGroupMode();

        var weapons = lease.Take(ControllablesBox);
        weapons.Name = "CockpitWeapons";
        weapons.VerticalExpand = true;
        weapons.MinimumRowHeight = 32;
        var supply = new WFGlassGauge("wf-cockpit-guns-flares", () =>
        {
            var reading = WFGaugeReading.Number(_wfDispenser.Connected ? _wfCombat?.Ammunition : null,
                0, WFGaugeScale.Ceiling(_wfCombat?.Ammunition ?? 0, 30), "wf-gauge-unit-rounds",
                tint: _wfCombat is { Ammunition: 0, UnlimitedSupply: false } ? Red : Accent);
            return _wfDispenser.Connected && _wfCombat is { UnlimitedSupply: true }
                ? reading with { Value = reading.Maximum, Text = Loc.GetString("wf-gauge-autoloader") } : reading;
        }, true) { SetHeight = 56, MinWidth = 0, Name = "CockpitFlareSupply" };
        var threats = new WFGlassGauge("wf-cockpit-guns-threats", () => WFGaugeReading.Number(
            _wfConnected ? _wfCombat?.Threats : null, 0, WFGaugeScale.Ceiling(_wfCombat?.Threats ?? 0, 5),
            "wf-gauge-unit-count", tint: _wfCombat?.Threats > 0 ? Red : Green), true) { SetHeight = 56, MinWidth = 0 };
        var cooldown = new WFGlassGauge("wf-cockpit-guns-cooldown", () => WFGaugeReading.Number(
            _wfDispenser.Connected ? _wfCombat?.Cooldown : null, 0, WFGaugeScale.Ceiling(_wfCombat?.Cooldown ?? 0, 15),
            "wf-gauge-unit-seconds", 1), true) { SetHeight = 56, MinWidth = 0 };
        var flares = Row(Borrow(_wfAutomatic), Borrow(_wfDispense), Borrow(RefreshButton, "wf-cockpit-guns-link"));
        _wfAutomatic.Name = "CockpitAutomaticFlares";
        _wfDispense.Name = "CockpitDispenseFlares";
        RefreshButton.Name = "CockpitGunneryRefresh";
        RefreshButton.HorizontalExpand = false;
        RefreshButton.SetWidth = 48;
        flares.SeparationOverride = 2;
        var readings = Row(supply, threats, cooldown);
        _wfCockpitCountermeasures = readings;
        readings.Visible = _wfCombat?.FlareLaunchers.Count > 0;
        readings.SeparationOverride = 2;
        var body = Column(select, memory, weapons, readings, flares);
        body.SeparationOverride = 3;
        body.HorizontalExpand = body.VerticalExpand = true;
        return body;
    }

    /// <summary>Keeps the saved group captions short without changing their existing handlers.</summary>
    internal void WfUpdateCockpitGunnery()
    {
        if (_wfCockpitStore == null)
            return;
        _wfCockpitStore.Disabled = !_wfConnected;
        if (_wfCockpitCountermeasures != null)
            _wfCockpitCountermeasures.Visible = _wfCombat?.FlareLaunchers.Count > 0;
        for (var i = 0; i < WFWeaponGroups.Count; i++)
        {
            _wfRecall[i].ToolTip = _wfRecall[i].Text;
            _wfRecall[i].Text = Loc.GetString("wf-cockpit-guns-group", ("slot", i + 1),
                ("count", _wfCombat?.Groups[i].Count ?? 0));
        }
    }

    private void WfShowCockpitGroupMode()
    {
        for (var i = 0; i < WFWeaponGroups.Count; i++)
        {
            _wfRecall[i].Visible = _wfCockpitStore?.Pressed != true;
            _wfSave[i].Visible = _wfCockpitStore?.Pressed == true;
        }
    }
}
