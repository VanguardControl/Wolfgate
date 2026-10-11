using Robust.Client.UserInterface;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Cockpit;

/// <summary>Keeps interactive MFD plots outside their separate scrolling detail region.</summary>
public static class WFCockpitMfdLayout
{
    /// <summary>Allocates a fixed share of the MFD to the plot without moving it when details scroll.</summary>
    public static Control Split(Control plot, Control details, float plotShare = 0.7f)
    {
        var plotHost = new Control { HorizontalExpand = true, VerticalExpand = true, SizeFlagsStretchRatio = plotShare };
        var detailsHost = new Control { HorizontalExpand = true, VerticalExpand = true, SizeFlagsStretchRatio = 1 - plotShare };
        plotHost.AddChild(plot);
        detailsHost.AddChild(details);
        var page = Column(plotHost, detailsHost);
        page.HorizontalExpand = page.VerticalExpand = true;
        return page;
    }

    /// <summary>Creates the only scrolling region for a page's plain controls and lists.</summary>
    public static Control Details(Control content)
    {
        content.VerticalAlignment = Control.VAlignment.Top;
        return Scroll(content);
    }
}
