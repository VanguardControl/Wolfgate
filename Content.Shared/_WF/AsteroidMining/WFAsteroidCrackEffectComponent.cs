using Robust.Shared.GameStates;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>A short-lived burst where a rock broke: flash, shockwave, flying chips and a settling dust cloud.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFAsteroidCrackEffectComponent : Component
{
    [DataField, AutoNetworkedField] public float Radius = 3f;

    /// <summary>The rock's leading ore, which tints some of the chips.</summary>
    [DataField, AutoNetworkedField] public Color VeinColor = Color.Gray;

    /// <summary>A blast or a weapon shatter rather than a clean crack: a hotter flash and faster chips.</summary>
    [DataField, AutoNetworkedField] public bool Violent;

    [DataField, AutoNetworkedField] public TimeSpan Start;

    [DataField] public float Duration = 2.2f;
}
