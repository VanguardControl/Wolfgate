using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared.Weather;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Planets;

/// <summary>Weather choices and the local clock for one planet type.</summary>
[Prototype("wfPlanetWeather")]
public sealed partial class WFPlanetWeatherPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public ProtoId<PlanetTypePrototype> PlanetType;
    [DataField(required: true)] public List<ProtoId<WeatherPrototype>> Weather = new();
    [DataField] public float ClearMinSeconds = 180f;
    [DataField] public float ClearMaxSeconds = 420f;
    [DataField] public float WeatherMinSeconds = 120f;
    [DataField] public float WeatherMaxSeconds = 240f;
    /// <summary>Real seconds per local day, shared by the watch, daylight, shadows and ambience.</summary>
    [DataField] public float DaySeconds = 3600f;
    [DataField] public float InitialHour = 8f;
}
