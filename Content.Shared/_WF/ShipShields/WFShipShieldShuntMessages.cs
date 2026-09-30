using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipShields;

/// <summary>A pilot commits a ship-local shield allocation at the helm.</summary>
[Serializable, NetSerializable]
public sealed class WFShipShieldSetShuntMessage : BoundUserInterfaceMessage
{
    /// <summary>Committed ship-local sector direction.</summary>
    public float DirectionRadians;
    /// <summary>Fraction of capacity redirected into the sector.</summary>
    public float Concentration;
    /// <summary>Width of the selected sector.</summary>
    public float ArcRadians;

    /// <summary>Creates one allocation request from the pilot's committed preview.</summary>
    public WFShipShieldSetShuntMessage(float directionRadians, float concentration, float arcRadians)
    {
        DirectionRadians = directionRadians;
        Concentration = concentration;
        ArcRadians = arcRadians;
    }
}

/// <summary>Authoritative allocation and health shared by every helm on the ship.</summary>
[Serializable, NetSerializable]
public sealed class WFShipShieldShuntState
{
    /// <summary>The ship has an emitter and accepts allocation settings.</summary>
    public bool Available;
    /// <summary>A shield field currently exists.</summary>
    public bool Active;
    /// <summary>Remaining shield capacity.</summary>
    public float Health;
    /// <summary>Current ship-local sector direction.</summary>
    public float DirectionRadians;
    /// <summary>Current redirected capacity fraction.</summary>
    public float Concentration;
    /// <summary>Current selected sector width.</summary>
    public float ArcRadians;

    /// <summary>Creates an authoritative snapshot for all helms on the ship.</summary>
    public WFShipShieldShuntState(bool available, bool active, float health, float directionRadians, float concentration, float arcRadians)
    {
        Available = available;
        Active = active;
        Health = health;
        DirectionRadians = directionRadians;
        Concentration = concentration;
        ArcRadians = arcRadians;
    }
}
