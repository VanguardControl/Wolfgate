using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.NpcCrew;

/// <summary>Admin operations supported by the crew setup window.</summary>
[Serializable, NetSerializable]
public enum WFCrewSetupAction : byte { List, Plan, Spawn, Clear, Orders, Preview, Teleport, SpawnVessel }

/// <summary>An editable role, loadout and local post on a selected ship.</summary>
[Serializable, NetSerializable]
public sealed class WFCrewSetupPost
{
    public string Role = "WFCrewDeckhand";
    public string Loadout = string.Empty;
    public Vector2 Position;
    public WFCrewEngagement? Engagement;
}

/// <summary>Mission settings shared by admin setup and encounter spawning.</summary>
[Serializable, NetSerializable]
public sealed class WFCrewMission
{
    public string Group = "crew";
    public string Callsign = string.Empty;
    public string Company = string.Empty;
    public string Faction = "WFCrew";
    public string LocalChannel = "Traffic";
    public string AlertChannel = "Common";
    public bool HeaveTo = true;
    public WFPilotOrder Order;
    public Vector2 Destination;
    public float Range = 60;
    public NetEntity? Target;
}

/// <summary>A request revalidated by the server, including the caller's admin permission.</summary>
[Serializable, NetSerializable]
public sealed class WFCrewSetupRequest : EntityEventArgs
{
    public WFCrewSetupAction Action;
    public NetEntity? Grid;
    public int Deckhands = 2;
    public bool Captain;
    public string Vessel = string.Empty;
    public List<WFCrewSetupPost> Posts = new();
    public WFCrewMission Mission = new();
}

/// <summary>A named grid visible to the admin tool.</summary>
[Serializable, NetSerializable]
public sealed record WFCrewSetupGrid(NetEntity Id, string Name);

/// <summary>Opens setup for a crew selected through an admin verb.</summary>
[Serializable, NetSerializable]
public sealed class WFCrewSetupOpenEvent(NetEntity grid, string group) : EntityEventArgs
{
    public NetEntity Grid = grid;
    public string Group = group;
}

/// <summary>The current server plan or a localized operation result.</summary>
[Serializable, NetSerializable]
public sealed class WFCrewSetupResponse : EntityEventArgs
{
    public WFCrewSetupAction Action;
    public NetEntity? Grid;
    public List<WFCrewSetupGrid> Grids = new();
    public List<WFCrewSetupPost> Posts = new();
    public string Message = string.Empty;
}
