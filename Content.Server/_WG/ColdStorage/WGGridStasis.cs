namespace Content.Server._WG.ColdStorage;

/// <summary>Raised on a grid when it starts going into stasis (true) and when it is woken (false).</summary>
[ByRefEvent]
public readonly record struct WGGridStasisChangedEvent(bool InStasis);

/// <summary>Which grids may enter stasis, by who owns them.</summary>
public enum WGStasisPolicy : byte
{
    /// <summary>Never: a planet, FTL, vehicle cabin or rock.</summary>
    Never,

    /// <summary>No deed: a wreck, derelict or debris hull. Everything but walls and cables is paused.</summary>
    Ownerless,

    /// <summary>A deeded ship: only with <see cref="WGStasisCVars.PlayerShips"/>, and only while its power is off.</summary>
    PlayerShip,

    /// <summary>
    /// A station or point of interest: only with <see cref="WGStasisCVars.Stations"/>. Most of them stand empty for
    /// most of a round.
    /// </summary>
    Station,

    /// <summary>An encounter or crewed NPC ship: only with <see cref="WGStasisCVars.NpcShips"/>, and never stored.</summary>
    NpcShip,
}

/// <summary>Where a grid is between awake and in stasis.</summary>
public enum WGGridStasisState : byte
{
    Awake,
    Pausing,
    Paused,
    Waking,
}

/// <summary>What <see cref="WGGridStasisSystem"/> knows about one grid.</summary>
public sealed class WGStasisGrid
{
    public EntityUid Grid;
    public WGStasisPolicy Policy;
    public WGGridStasisState State;

    /// <summary>Since when nobody has attended it; null while someone does.</summary>
    public TimeSpan? IdleSince;

    /// <summary>When it began going into stasis; null while awake or waking.</summary>
    public TimeSpan? StasisSince;

    /// <summary>What <see cref="StasisSince"/> was when it began to wake, until the time away is applied.</summary>
    public TimeSpan? AwaySince;

    /// <summary>Metres to the nearest attendee of its dock group at the last look; 0 for someone aboard.</summary>
    public float Distance = float.MaxValue;

    /// <summary>Woken whole in one tick, whatever it costs.</summary>
    public bool Urgent;

    /// <summary>What this grid paused, in the order it went: mobs last.</summary>
    public readonly List<EntityUid> Paused = new();

    /// <summary>When each entity that turned up after the grid was paused was paused itself.</summary>
    public readonly Dictionary<EntityUid, TimeSpan> Late = new();

    /// <summary>The grid's own children to pause, things first and mobs after.</summary>
    public readonly List<EntityUid> Tops = new();

    public int Cursor;
    public bool Gathered;
    public TimeSpan NextSweep;

    /// <summary>The map the grid was on when it began going into stasis.</summary>
    public EntityUid? Map;
}
