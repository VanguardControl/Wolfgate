using System.Linq;
using Content.Shared.Pinpointer;

namespace Content.Server.Pinpointer;

public sealed partial class NavMapSystem
{
    /// <summary>Most removed chunks a nav map remembers before it drops the oldest half.</summary>
    public const int WFRemovedLimit = 256;

    /// <summary>Drops the oldest removals once there are too many, and notes how far the history now reaches.</summary>
    private void WFTrimRemovedChunks(NavMapComponent component)
    {
        if (component.WFRemovedChunks.Count <= WFRemovedLimit)
            return;

        var drop = component.WFRemovedChunks.Count - WFRemovedLimit / 2;
        foreach (var (origin, tick) in component.WFRemovedChunks.OrderBy(r => r.Value).Take(drop).ToList())
        {
            component.WFRemovedChunks.Remove(origin);
            if (tick > component.WFRemovedTrimmedTo)
                component.WFRemovedTrimmedTo = tick;
        }
    }
}
