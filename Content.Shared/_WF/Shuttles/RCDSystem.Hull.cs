using Content.Shared._WF.Shuttles;

namespace Content.Shared.RCD.Systems;

public partial class RCDSystem
{
    /// <summary>Records successful RCD removal separately from damage or arbitrary entity deletion.</summary>
    private void WfRecordHullDeconstruction(EntityUid target)
    {
        var ev = new WFHullDeconstructedEvent();
        RaiseLocalEvent(target, ref ev);
    }
}
