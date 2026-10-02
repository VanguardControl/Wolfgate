namespace Content.Shared._WF.NpcCrew;

/// <summary>What a crew pilot does with the ship once it has the helm.</summary>
public enum WFPilotOrder : byte
{
    /// <summary>Stop the ship and keep station where it is.</summary>
    Hold,

    /// <summary>Fly the waypoints in turn, then hold.</summary>
    GoTo,

    /// <summary>Circle a point at a radius.</summary>
    Loiter,

    /// <summary>Keep within range of another grid.</summary>
    Follow,
}
