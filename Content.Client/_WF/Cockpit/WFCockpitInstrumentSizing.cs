using Robust.Client.UserInterface;

namespace Content.Client._WF.Cockpit;

/// <summary>Keeps digital and mechanical cockpit instruments equally readable.</summary>
public static class WFCockpitInstrumentSizing
{
    /// <summary>Applies the shared dial height and restores borrowed controls when the cockpit closes.</summary>
    public static void Bind(Control instrument, WFCockpitLease lease, float height)
    {
        var previous = instrument.SetHeight;
        instrument.SetHeight = height;
        lease.Remember(() => instrument.SetHeight = previous);
    }
}
