using Robust.Shared.Player;

namespace Content.Server._WF.MappingTools;

/// <summary>
/// Raised when a player without the Mapping admin flag uses the mapping tools; a handler sets
/// <see cref="Allowed"/> to let them. <see cref="Request"/> is set for requests the player sends, so a handler can
/// rate-limit them.
/// </summary>
public sealed class WFMappingCanUseEvent(ICommonSession session, bool request) : EntityEventArgs
{
    public readonly ICommonSession Session = session;
    public readonly bool Request = request;
    public bool Allowed;
}

/// <summary>What a mapping tools request does to the map it targets.</summary>
public enum WFMappingEditKind : byte
{
    /// <summary>Reads it, as Copy does.</summary>
    Read,

    /// <summary>Changes what is already there: move, mirror, cut, delete, undo and redo.</summary>
    Edit,

    /// <summary>Adds the clipboard's contents.</summary>
    Paste,

    /// <summary>Adds a z-level next to it.</summary>
    AddLevel,

    /// <summary>Deletes it from its z-stack.</summary>
    RemoveLevel,
}

/// <summary>
/// Raised before a mapping tools request changes or reads a map; cancelling it drops the request. The canceller tells
/// the player why. Nothing cancels it outside mapping mode.
/// </summary>
public sealed class WFMappingCanEditEvent(ICommonSession session, EntityUid map, WFMappingEditKind kind)
    : CancellableEntityEventArgs
{
    public readonly ICommonSession Session = session;
    public readonly EntityUid Map = map;
    public readonly WFMappingEditKind Kind = kind;

    /// <summary>The grid the request works on, or null for a new grid.</summary>
    public EntityUid? Grid;

    /// <summary>The cells the result covers: local to <see cref="Grid"/>, or map tiles when it is null.</summary>
    public Box2i? Cells;

    /// <summary>Entities the request adds.</summary>
    public int Entities;

    /// <summary>Tiles the request touches.</summary>
    public int Tiles;

    /// <summary>The request makes a new grid.</summary>
    public bool NewGrid;

    /// <summary>The cell of each root entity the request adds, in the same frame as <see cref="Cells"/>.</summary>
    public List<Vector2i>? EntityCells;

    /// <summary>Only asks; the canceller doesn't tell the player.</summary>
    public bool Quiet;

    /// <summary>The Fluent id of the refusal, when cancelled.</summary>
    public string? Reason;
}

/// <summary>Raised after the levels of a z-stack change, with the levels bottom-up and the index of depth 0.</summary>
public sealed class WFMappingStackChangedEvent(List<EntityUid> levels, int ground) : EntityEventArgs
{
    public readonly List<EntityUid> Levels = levels;
    public readonly int Ground = ground;
}

/// <summary>Raised after the mapping tools change a map.</summary>
public sealed class WFMappingEditedEvent(EntityUid map) : EntityEventArgs
{
    public readonly EntityUid Map = map;
}
