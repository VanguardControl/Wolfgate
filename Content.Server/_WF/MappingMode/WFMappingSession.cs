using Content.Shared._WF.MappingMode;
using Robust.Shared.Network;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// A shared mapping session: its members, the observers of those present, and the map its z-stack grows from.
/// </summary>
public sealed class WFMappingSession(int id, string code, string name, NetUserId owner, EntityUid rootMap)
{
    public readonly int Id = id;
    public readonly string Code = code;
    public string Name = name;
    public NetUserId Owner = owner;

    /// <summary>Members, with the time each joined; absent members stay members until they leave.</summary>
    public readonly Dictionary<NetUserId, TimeSpan> Members = new();

    /// <summary>The observer of each member in the session right now.</summary>
    public readonly Dictionary<NetUserId, EntityUid> Present = new();

    /// <summary>The ground level; the session's other levels share its z-network.</summary>
    public EntityUid RootMap = rootMap;

    /// <summary>Changed since the last save.</summary>
    public bool Dirty;

    public DateTime? LastSaved;
    public TimeSpan NextAutosave;

    /// <summary>When the last present member went, while nobody is present.</summary>
    public TimeSpan? EmptySince;

    /// <summary>The library map and version it was opened from, if any.</summary>
    public string? SourceMap;
    public string? SourceVersion;

    /// <summary>Entities across the session's maps, as last counted, plus placements since.</summary>
    public int EntityCount;
    public TimeSpan CountedAt;

    /// <summary>The limits last sent to its members.</summary>
    public WFMappingLimitsState? SentLimits;
}
