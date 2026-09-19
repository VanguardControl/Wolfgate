using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Weather;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WF.PlanetCracker.Planets;

/// <summary>One weather scheduler and clock epoch per world, not per tile or player.</summary>
[RegisterComponent, UnsavedComponent, AutoGenerateComponentPause]
public sealed partial class WFPlanetWeatherComponent : Component
{
    [DataField] public ProtoId<WFPlanetWeatherPrototype> Profile;
    [DataField] public string PlanetName = string.Empty;
    [DataField] public ProtoId<WeatherPrototype>? Current;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan Epoch;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextChange;
}

/// <summary>Remembers the state copied onto a static or newly-created transit map.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetWeatherAppliedComponent : Component
{
    public ProtoId<WeatherPrototype>? Current;
    public TimeSpan EndTime;
}
