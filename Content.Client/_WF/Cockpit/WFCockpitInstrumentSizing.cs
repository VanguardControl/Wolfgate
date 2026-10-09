using Content.Client._WF.CombatConsole;
using Content.Shared._WF.CCVar;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;

namespace Content.Client._WF.Cockpit;

/// <summary>Gives mechanical cockpit dials more space while preserving the digital instrument layout.</summary>
public static class WFCockpitInstrumentSizing
{
    /// <summary>Resizes an instrument with the theme and releases the subscription when the cockpit closes.</summary>
    public static void Bind(Control instrument, WFCockpitLease lease, float digitalHeight, float retroHeight)
    {
        var configuration = IoCManager.Resolve<IConfigurationManager>();
        void Changed(string _) => instrument.SetHeight = WFInstrumentTheme.Digital ? digitalHeight : retroHeight;
        configuration.OnValueChanged(WolfgateCVars.UiStyle, Changed, true);
        lease.Remember(() => configuration.UnsubValueChanged(WolfgateCVars.UiStyle, Changed));
    }
}
