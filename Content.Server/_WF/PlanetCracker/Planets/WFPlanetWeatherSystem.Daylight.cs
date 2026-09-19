using Content.Server.GameTicking;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Planets;

public sealed partial class WFPlanetWeatherSystem
{
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedLightCycleSystem _lightCycles = default!;
    [Dependency] private MetaDataSystem _metadata = default!;

    private void SynchronizeDaylight(EntityUid map, WFPlanetWeatherComponent state,
        WFPlanetWeatherPrototype profile, EntityUid? lowerMap = null)
    {
        if (HasComp<WFOrbitLayerComponent>(map))
            return;
        var light = EnsureComp<MapLightComponent>(map);
        var cycle = EnsureComp<LightCycleComponent>(map);
        var duration = TimeSpan.FromSeconds(Math.Max(1, profile.DaySeconds));
        // Stock lighting evaluates CurTime + Offset - RoundStart - map pause time.
        // The watch evaluates CurTime - Epoch + InitialHour; express precisely that same clock.
        var offset = _ticker.RoundStartTimeSpan - state.Epoch + _metadata.GetPauseTime(map) +
            TimeSpan.FromSeconds(duration.TotalSeconds * profile.InitialHour / 24);
        var original = cycle.OriginalColor == Color.Transparent ? light.AmbientLightColor : cycle.OriginalColor;
        if (lowerMap is { } lower && TryComp<LightCycleComponent>(lower, out var lowerCycle))
            original = lowerCycle.OriginalColor;
        if (cycle.Duration != duration || cycle.InitialOffset || !cycle.Enabled ||
            cycle.OriginalColor != original || cycle.MinLightLevel != 0.12f)
        {
            cycle.Duration = duration;
            cycle.InitialOffset = false;
            cycle.Enabled = true;
            cycle.OriginalColor = original;
            cycle.MinLightLevel = 0.12f;
            Dirty(map, cycle);
        }
        if (cycle.Offset != offset)
            _lightCycles.SetOffset((map, cycle), offset);

        // SetOffset also updates shadow offset, but their duration must be synchronized explicitly.
        if (TryComp<SunShadowCycleComponent>(map, out var shadows) &&
            (shadows.Duration != duration || shadows.Offset != offset))
        {
            shadows.Duration = duration;
            shadows.Offset = offset;
            Dirty(map, shadows);
        }
    }
}
