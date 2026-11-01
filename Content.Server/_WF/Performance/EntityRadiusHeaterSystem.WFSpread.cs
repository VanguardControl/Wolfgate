using Content.Server._Mono.Temperature.Components;
using Content.Shared._WF.Performance;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Temperature.Systems;

public sealed partial class EntityRadiusHeaterSystem
{
    /// <summary>Moves to the next heater whose tick of the second this is.</summary>
    private static bool WFNextDue(ref EntityQueryEnumerator<EntityRadiusHeaterComponent> query,
        GameTick tick,
        int period,
        out EntityUid uid,
        out EntityRadiusHeaterComponent comp)
    {
        while (query.MoveNext(out uid, out comp!))
        {
            if (WFTickSpread.Due(uid, tick, period))
                return true;
        }

        return false;
    }
}
