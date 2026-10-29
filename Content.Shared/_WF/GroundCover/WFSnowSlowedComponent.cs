using Robust.Shared.GameStates;

namespace Content.Shared._WF.GroundCover;

/// <summary>On a mob wading through deep snow: its walking speed share there.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFSnowSlowedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Speed = 1f;
}
