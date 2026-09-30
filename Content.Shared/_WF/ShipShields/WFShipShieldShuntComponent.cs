using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.ShipShields;

/// <summary>Allocates shield capacity to a grid-local angular sector.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class WFShipShieldShuntComponent : Component
{
    /// <summary>Direction measured counterclockwise from grid-local east.</summary>
    [DataField, AutoNetworkedField]
    public float DirectionRadians = MathF.PI / 2f;

    /// <summary>Fraction of the other sectors' power transferred into this sector.</summary>
    [DataField, AutoNetworkedField]
    public float Concentration;

    /// <summary>Angular width of the reinforced sector.</summary>
    [DataField, AutoNetworkedField]
    public float ArcRadians = MathF.PI / 2f;

    /// <summary>Hull center in grid-local coordinates.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 Center;
}
