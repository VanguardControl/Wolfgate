namespace Content.Server._WF.ShipShields;

/// <summary>Limits impact audio independently for each shield.</summary>
[RegisterComponent]
public sealed partial class WFShipShieldImpactAudioComponent : Component
{
    /// <summary>Earliest time another impact sound may start.</summary>
    public TimeSpan NextImpactSound;
}
