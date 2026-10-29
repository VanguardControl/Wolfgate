using Content.Shared._WF.Planets;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Planets;

/// <summary>Phased storms: telegraph, main, end. Lightning comes from the world's WolfWeather for the phase.</summary>
public sealed partial class WFPlanetWeatherSystem
{
    /// <summary>Moves a world's storm on one phase: clear, telegraph, main, end, clear.</summary>
    private void AdvanceStorm(WFPlanetWeatherComponent state, WFPlanetWeatherPrototype profile)
    {
        var storm = state.Storm >= 0 && state.Storm < profile.Storms.Count ? profile.Storms[state.Storm] : null;

        switch (state.Phase)
        {
            case WFStormPhase.None when profile.Storms.Count > 0:
                state.Storm = _random.Next(profile.Storms.Count);
                storm = profile.Storms[state.Storm];

                if (storm.Telegraph is { } telegraph)
                {
                    Enter(state, WFStormPhase.Telegraph, telegraph, storm.TelegraphSeconds);
                    return;
                }

                EnterMain(state, storm, profile);
                return;

            case WFStormPhase.Telegraph when storm != null:
                EnterMain(state, storm, profile);
                return;

            case WFStormPhase.Main when storm?.End is { } end:
                Enter(state, WFStormPhase.End, end, storm.EndSeconds);
                return;

            default:
                Enter(state, WFStormPhase.None, null, _random.NextFloat(profile.ClearMinSeconds, profile.ClearMaxSeconds));
                return;
        }
    }

    private void EnterMain(WFPlanetWeatherComponent state, WFPlanetStorm storm, WFPlanetWeatherPrototype profile)
    {
        Enter(state, WFStormPhase.Main, storm.Main, _random.NextFloat(profile.WeatherMinSeconds, profile.WeatherMaxSeconds));
    }

    private void Enter(WFPlanetWeatherComponent state, WFStormPhase phase, ProtoId<Content.Shared.Weather.WeatherPrototype>? weather, float seconds)
    {
        state.Phase = phase;
        state.Current = weather;
        state.NextChange = _timing.CurTime + TimeSpan.FromSeconds(seconds);
    }
}
