using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.NpcCrew;

/// <summary>Admin operations supported by the crew setup window; Crews returns only the crew snapshot.</summary>
[Serializable, NetSerializable]
public enum WFCrewSetupAction : byte { List, Plan, Spawn, Clear, Orders, Preview, Teleport, SpawnVessel, Objectives, AppendObjective, Pause, Resume, Skip, Rules, Crews }

/// <summary>High-level crew tasks; timed tasks with zero duration continue until skipped.</summary>
[Serializable, NetSerializable]
public enum WFCrewObjectiveKind : byte { Hold, GoTo, Dock, Undock, Loiter, Follow, Attack, Retreat, Repair, Resupply, Salvage, Escort, Circle }

/// <summary>Response to an unauthorized docking or boarding incident.</summary>
[Serializable, NetSerializable]
public enum WFCrewSecurityResponse : byte { Ignore, Warn, Hostile }

/// <summary>A queued task with a grid or map-coordinate destination.</summary>
[Serializable, NetSerializable]
public sealed class WFCrewObjective
{
    public WFCrewObjectiveKind Kind;
    public NetEntity? Target;
    public Vector2 Position;
    public float Range = 100;
    public float Duration;
}

/// <summary>A live crew and its remaining objective queue.</summary>
[Serializable, NetSerializable]
public sealed class WFCrewSetupCrew
{
    public NetEntity Grid;
    public string Group = string.Empty;
    public int Members;
    public int Alive;
    public string Status = string.Empty;
    public string Activity = string.Empty;
    public NetEntity? ActivityTarget;
    public WFCrewMission Settings = new();
    public List<WFCrewObjective> Objectives = new();
}

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
    public string Battlegroup = string.Empty;
    public string Company = string.Empty;
    public string Faction = "WFCrew";
    public string LocalChannel = "Traffic";
    public string AlertChannel = "Common";
    public bool HeaveTo = true;
    public WFCrewSecurityResponse BoardingResponse = WFCrewSecurityResponse.Hostile;
    public WFCrewSecurityResponse DockingResponse = WFCrewSecurityResponse.Hostile;
    public WFCrewNavigationSettings Navigation = new();
    public WFPilotOrder Order;
    public Vector2 Destination;
    public float Range = 60;
    public NetEntity? Target;
}

/// <summary>A request revalidated by the server, including the caller's admin permission.</summary>
[Serializable, NetSerializable]
public sealed class WFCrewSetupRequest : EntityEventArgs
{
    public int RequestId;
    public WFCrewSetupAction Action;
    public NetEntity? Grid;
    public int Deckhands = 2;
    public bool Captain;
    public string Vessel = string.Empty;
    public List<WFCrewSetupPost> Posts = new();
    public WFCrewMission Mission = new();
    public List<WFCrewObjective> Objectives = new();
}

/// <summary>A named grid visible to the admin tool; Large grids cannot be planned or crewed.</summary>
[Serializable, NetSerializable]
public sealed record WFCrewSetupGrid(NetEntity Id, string Name, bool Large = false);

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
    public int RequestId;
    public WFCrewSetupAction Action;
    public NetEntity? Grid;
    public List<WFCrewSetupGrid> Grids = new();
    public List<WFCrewSetupPost> Posts = new();
    public string Message = string.Empty;
    public List<WFCrewSetupCrew> Crews = new();
}
