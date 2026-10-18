using Robust.Shared.Configuration;

namespace Content.Shared._WF.MappingMode;

/// <summary>Settings for mapping mode, the server-wide shared mapping workshop.</summary>
[CVarDefs]
public sealed class WFMappingModeCVars
{
    /// <summary>Turns the server into a mapping workshop: no rounds, a mapping lobby, per-player sessions.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.mapping_mode.enabled", false, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Side of the square, in tiles and centred on the origin, that every level's tiles must lie in.</summary>
    public static readonly CVarDef<int> MapSize =
        CVarDef.Create("wf.mapping_mode.map_size", 256, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Default seconds between autosaves of a dirty session.</summary>
    public static readonly CVarDef<int> AutosaveInterval =
        CVarDef.Create("wf.mapping_mode.autosave_interval", 120, CVar.SERVERONLY);

    /// <summary>Shortest autosave interval a session owner can pick, in seconds.</summary>
    public static readonly CVarDef<int> AutosaveMinInterval =
        CVarDef.Create("wf.mapping_mode.autosave_min_interval", 60, CVar.SERVERONLY);

    /// <summary>Longest autosave interval a session owner can pick, in seconds.</summary>
    public static readonly CVarDef<int> AutosaveMaxInterval =
        CVarDef.Create("wf.mapping_mode.autosave_max_interval", 900, CVar.SERVERONLY);

    /// <summary>Versions kept per library map.</summary>
    public static readonly CVarDef<int> MaxVersions =
        CVarDef.Create("wf.mapping_mode.max_versions", 25, CVar.SERVERONLY);

    /// <summary>Maps per player library.</summary>
    public static readonly CVarDef<int> MaxMaps =
        CVarDef.Create("wf.mapping_mode.max_maps", 50, CVar.SERVERONLY);

    /// <summary>Bytes per player library.</summary>
    public static readonly CVarDef<long> MaxLibraryBytes =
        CVarDef.Create("wf.mapping_mode.max_library_bytes", 256L * 1024 * 1024, CVar.SERVERONLY);

    /// <summary>Members per session, absent ones included.</summary>
    public static readonly CVarDef<int> MaxMembers =
        CVarDef.Create("wf.mapping_mode.max_members", 8, CVar.SERVERONLY);

    /// <summary>Live sessions on the server.</summary>
    public static readonly CVarDef<int> MaxSessions =
        CVarDef.Create("wf.mapping_mode.max_sessions", 32, CVar.SERVERONLY);

    /// <summary>Z-levels per session.</summary>
    public static readonly CVarDef<int> MaxLevels =
        CVarDef.Create("wf.mapping_mode.max_levels", 3, CVar.SERVERONLY);

    /// <summary>Seconds a session with nobody present survives before it closes.</summary>
    public static readonly CVarDef<int> EmptySessionGrace =
        CVarDef.Create("wf.mapping_mode.empty_session_grace", 300, CVar.SERVERONLY);

    /// <summary>Grids each member adds to their session's grid cap.</summary>
    public static readonly CVarDef<int> GridsPerPlayer =
        CVarDef.Create("wf.mapping_mode.grids_per_player", 2, CVar.SERVERONLY);

    /// <summary>Grids per session, whatever the member count.</summary>
    public static readonly CVarDef<int> MaxGridsPerSession =
        CVarDef.Create("wf.mapping_mode.max_grids_per_session", 8, CVar.SERVERONLY);

    /// <summary>Entities on one tile.</summary>
    public static readonly CVarDef<int> MaxEntitiesPerTile =
        CVarDef.Create("wf.mapping_mode.max_entities_per_tile", 30, CVar.SERVERONLY);

    /// <summary>Decals on one tile.</summary>
    public static readonly CVarDef<int> MaxDecalsPerTile =
        CVarDef.Create("wf.mapping_mode.max_decals_per_tile", 16, CVar.SERVERONLY);

    /// <summary>Entities across all of a session's maps.</summary>
    public static readonly CVarDef<int> MaxEntities =
        CVarDef.Create("wf.mapping_mode.max_entities", 25000, CVar.SERVERONLY);

    /// <summary>Size of a file loaded as a reference grid.</summary>
    public static readonly CVarDef<long> MaxReferenceBytes =
        CVarDef.Create("wf.mapping_mode.max_reference_bytes", 8L * 1024 * 1024, CVar.SERVERONLY);

    /// <summary>Placement, tile and decal requests per second per mapper.</summary>
    public static readonly CVarDef<int> MaxEditsPerSecond =
        CVarDef.Create("wf.mapping_mode.max_edits_per_second", 60, CVar.SERVERONLY);
}
