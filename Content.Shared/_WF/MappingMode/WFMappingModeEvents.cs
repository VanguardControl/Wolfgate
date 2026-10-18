using System.Text.RegularExpressions;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode;

/// <summary>Limits on session names and other text a mapper sends.</summary>
public static class WFMappingModeLimits
{
    public const int MaxNameLength = 48;

    /// <summary>Longest file path a reference request may name.</summary>
    public const int MaxPathLength = 256;

    private static readonly Regex NamePattern = new("^[A-Za-z0-9 _-]{1,48}$", RegexOptions.Compiled);

    /// <summary>Whether a trimmed name is 1 to 48 letters, digits, spaces, dashes or underscores.</summary>
    public static bool IsValidName(string name)
    {
        return NamePattern.IsMatch(name);
    }
}

/// <summary>Creates a new session on an empty map and joins it.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingCreateRequest(string name) : EntityEventArgs
{
    public readonly string Name = name;
}

/// <summary>Joins a live session by its code.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingJoinRequest(string code) : EntityEventArgs
{
    public readonly string Code = code;
}

/// <summary>Goes back into a session the player is still a member of.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingRejoinRequest(int sessionId) : EntityEventArgs
{
    public readonly int SessionId = sessionId;
}

/// <summary>Leaves the player's session for good and returns them to the lobby.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingLeaveRequest : EntityEventArgs;

/// <summary>The session owner removes a member.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingKickRequest(NetUserId user) : EntityEventArgs
{
    public readonly NetUserId User = user;
}

/// <summary>A session member as its members see it.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingMemberInfo(NetUserId User, string Name, bool Present);

/// <summary>A session the player can go back into from the lobby.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingRejoinInfo(int Id, string Name, string OwnerName, int MemberCount);

/// <summary>The current counts and caps of a session, for the Mapping window's status line.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingLimitsState(int Grids, int MaxGrids, int Entities, int MaxEntities, int MapSize);

/// <summary>The player's own session, shown only to its members.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingSessionInfo
{
    public int Id;
    public string Name = string.Empty;
    public string Code = string.Empty;
    public NetUserId Owner;
    public List<WFMappingMemberInfo> Members = new();
    public WFMappingLimitsState Limits;
}

/// <summary>
/// Server to client: the player's session, if they are in one, and the sessions they can rejoin.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingSessionStateEvent(WFMappingSessionInfo? session, List<WFMappingRejoinInfo> rejoinable)
    : EntityEventArgs
{
    public readonly WFMappingSessionInfo? Session = session;
    public readonly List<WFMappingRejoinInfo> Rejoinable = rejoinable;
}

/// <summary>A Fluent argument: text, or a number when <see cref="Text"/> is null.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingLocArg(string Name, string? Text, long Number);

/// <summary>
/// Server to client: the outcome of a request or a refused edit, as a Fluent id and its arguments.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingResultEvent(string locKey, List<WFMappingLocArg> args, bool success) : EntityEventArgs
{
    public readonly string LocKey = locKey;
    public readonly List<WFMappingLocArg> Args = args;
    public readonly bool Success = success;

    /// <summary>The arguments in the form <c>Loc.GetString</c> takes.</summary>
    public (string, object)[] LocArgs()
    {
        var result = new (string, object)[Args.Count];
        for (var i = 0; i < Args.Count; i++)
        {
            var arg = Args[i];
            result[i] = (arg.Name, arg.Text ?? (object) arg.Number);
        }

        return result;
    }
}
