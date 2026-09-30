using Content.Shared.Light;

namespace Content.Client._WF.LightFlicker;

/// <summary>
/// Client-side flicker state of a powered light.
/// </summary>
[RegisterComponent, Access(typeof(LightFlickerSystem))]
public sealed partial class LightFlickerComponent : Component
{
    /// <summary>Bulb state from the last appearance change, to tell a light switching on from one already lit.</summary>
    public PoweredLightState? LastState;

    public LightFlickerMode Mode;

    public LightFlickerPhase Phase;

    /// <summary>Real time of the next step in the pattern.</summary>
    public TimeSpan NextStep;

    /// <summary>Whether the flicker last left the light lit.</summary>
    public bool Lit;

    /// <summary>Flashes left in a turn-on strike, counting the last one that stays lit.</summary>
    public int FlashesLeft;
}

public enum LightFlickerMode : byte
{
    None,

    /// <summary>A flash or two as the tube strikes on, then steady.</summary>
    Strike,

    /// <summary>Damaged ballast: long dark spells broken by short lit ones, until repaired.</summary>
    Fault,
}

public enum LightFlickerPhase : byte
{
    Dark,
    Strike,
    Hold,
}
