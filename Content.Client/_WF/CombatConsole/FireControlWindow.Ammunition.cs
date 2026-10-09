using System.Linq;
using Content.Client._WF.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.CombatConsole;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    private WFGaugeReading WfSupplyReading(NetEntity uid)
    {
        var entry = _currentState?.FireControllables?.FirstOrDefault(weapon => weapon.NetEntity == uid);
        if (!_wfConnected)
            return WFGaugeReading.Number(null, 0, 1, "wf-gauge-unit-rounds");
        if (_currentState is not { } state || !state.Combat.WeaponSupplies.TryGetValue(uid, out var supply))
        {
            var count = entry?.AmmoCount;
            return WFGaugeReading.Number(count, 0, WFGaugeScale.Ceiling(count ?? 0, 30),
                "wf-gauge-unit-rounds", tint: count == 0 ? Red : Accent);
        }
        if (supply.Kind == WFWeaponSupplyKind.Infinite)
            return new WFGaugeReading(1, 0, 1, Loc.GetString("wf-gauge-unlimited"), Accent);
        if (supply.Kind == WFWeaponSupplyKind.Energy)
        {
            double? charge = supply.Count is { } count && supply.Capacity is > 0 ? count * 100d / supply.Capacity : null;
            return WFGaugeReading.Number(charge, 0, 100, "wf-gauge-unit-percent", tint: charge == 0 ? Red : Accent);
        }
        var reading = WFGaugeReading.Number(supply.Count, 0, Math.Max(1, (double?) supply.Capacity ?? WFGaugeScale.Ceiling(supply.Count ?? 0, 30)),
            "wf-gauge-unit-rounds", tint: supply.Count == 0 && supply.Kind != WFWeaponSupplyKind.Recharging ? Red : Accent);
        return supply is { Kind: WFWeaponSupplyKind.Recharging, Count: 0 }
            ? reading with { Text = Loc.GetString("wf-gauge-recharging") } : reading;
    }

    private bool WfUpdateWeaponSupplyText(Button button, FireControllableEntry controllable)
    {
        var reading = WfSupplyReading(controllable.NetEntity);
        button.Text = Loc.GetString("wf-console-weapon-label", ("name", controllable.Name), ("supply", reading.Text));
        button.ToolTip = Loc.GetString("wf-console-weapon-supply", ("name", controllable.Name), ("supply", reading.Text));
        button.ModulateSelfOverride = reading.Tint == Red ? Red : null;
        return true;
    }
}
