using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Sits on a concussed body for as long as any part carries a concussion wound. Networked because the blur
/// it contributes is recomputed client-side whenever eyewear changes.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedConcussionComponent : Component
{
    /// <summary>Blur the worst concussion is currently worth, added to the patient's eye damage.</summary>
    [DataField, AutoNetworkedField]
    public float Blur;

    /// <summary>Whether speech is slurred at the current severity.</summary>
    [DataField, AutoNetworkedField]
    public bool Stutter;

    /// <summary>Seconds of recovery not yet spent, so a slow tick still heals at the right rate.</summary>
    [DataField]
    public float Accumulator;
}
