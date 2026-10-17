using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.GodPanel;

/// <summary>Where a player stands in the round.</summary>
[Serializable, NetSerializable]
public enum WFGodPlayerStatus : byte
{
    Alive,
    Critical,
    Dead,
    Ghost,
    Lobby,
    Disconnected,
}

/// <summary>What a listed grid is.</summary>
[Serializable, NetSerializable]
public enum WFGodGridKind : byte
{
    Station,
    Ship,
    Encounter,
    Debris,
}

/// <summary>The timeline filters.</summary>
[Serializable, NetSerializable]
public enum WFGodEventCategory : byte
{
    Death,
    Player,
    Ship,
    Encounter,
    Sector,
    Round,
    Gm,
}

/// <summary>The round-wide numbers shown in the panel's top bar.</summary>
[Serializable, NetSerializable]
public sealed record WFGodRound(
    TimeSpan RoundTime,
    bool InRound,
    bool WarHot,
    int ReaverTier,
    string ReaverTierName,
    int ReaverCells,
    bool ReaversPaused,
    int EncountersRunning,
    bool EncountersPaused,
    string? SoundTitle,
    string? SoundRequester,
    int Gms);

/// <summary>One player, with the body or ghost they are in. Positions are relative to the grid, else the map.</summary>
[Serializable, NetSerializable]
public sealed record WFGodPlayer(
    NetUserId User,
    NetEntity? Entity,
    string Username,
    string Character,
    string Job,
    bool Antag,
    string AntagRole,
    NetEntity? Grid,
    string GridName,
    NetEntity? Map,
    NetCoordinates? Coordinates,
    WFGodPlayerStatus Status,
    float Health,
    bool Admin,
    bool Offered);

/// <summary>One grid worth listing: a station, a ship with a deed, players or crew, or an encounter's ship.</summary>
[Serializable, NetSerializable]
public sealed record WFGodGrid(
    NetEntity Entity,
    string Name,
    WFGodGridKind Kind,
    NetEntity? Map,
    NetCoordinates Coordinates,
    string Owner,
    int Players,
    int Crew,
    bool HasPower,
    float PowerFraction,
    int BreakersOn,
    int Breakers,
    string Code,
    bool GeneralQuarters,
    bool Distress,
    NetEntity? Encounter,
    string EncounterName,
    string EncounterState,
    string DockedTo,
    bool Sanctuary);

/// <summary>One timeline entry. <c>Time</c> is the round time it happened at.</summary>
[Serializable, NetSerializable]
public sealed record WFGodEvent(
    int Id,
    TimeSpan Time,
    WFGodEventCategory Category,
    string Text,
    NetEntity? Subject,
    NetCoordinates? Coordinates);

/// <summary>What the server sends a subscribed GM; the events are only those after the last id they received.</summary>
[Serializable, NetSerializable]
public sealed record WFGodPanelState(
    WFGodRound Round,
    List<WFGodPlayer> Players,
    List<WFGodGrid> Grids,
    List<WFGodEvent> Events,
    int LastEventId);
