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

    /// <summary>Times the tube has left to light in the current flicker; the last one stays lit.</summary>
    public int FlashesLeft;
}

public enum LightFlickerMode : byte
{
    None,

    /// <summary>A flash or two as the tube strikes on, then steady.</summary>
    Strike,

    /// <summary>Damaged ballast: lit, but flickering out every few seconds until repaired.</summary>
    Fault,
}

public enum LightFlickerPhase : byte
{
    /// <summary>Flickering off and on.</summary>
    Strike,

    /// <summary>Steadily lit until the next flicker.</summary>
    Hold,
}
