using Content.Shared._WF.Performance;

namespace Content.Client._Mono.Radar;

public sealed partial class RadarBlipsSystem
{
    /// <summary>Drops every blip the server removed this tick.</summary>
    private void WFRemoveBlips(WFBlipRemovalBatchEvent args)
    {
        _blips.RemoveAll(blip => args.Blips.Contains(blip.Uid));
    }
}
