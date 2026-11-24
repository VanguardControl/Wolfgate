using Robust.Shared.Configuration;

namespace Content.Shared._WG.Wilderness;

/// <summary>Rules and display settings for zones and the Wilderness mark.</summary>
[CVarDefs]
public sealed class WildernessCVars
{
    /// <summary>
    /// Whether staying in a Wilderness marks someone not aboard a ship. Off: only ships are marked for staying; people
    /// are still marked by flying with a marked ship or pilot, or by attacking into a Wilderness.
    /// </summary>
    public static readonly CVarDef<bool> SkullOnFoot =
        CVarDef.Create("wg.wilderness.skull_on_foot", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Seconds in a Wilderness before a ship or person is marked.</summary>
    public static readonly CVarDef<float> DwellSeconds =
        CVarDef.Create("wg.wilderness.dwell_seconds", 15f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Minutes the mark lasts after leaving a Wilderness.</summary>
    public static readonly CVarDef<float> SkullMinutes =
        CVarDef.Create("wg.wilderness.skull_minutes", 5f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>A HUD setting's value: shown everywhere it applies.</summary>
    public const string ShowAlways = "always";

    /// <summary>A HUD setting's value: shown only in danger, meaning a Wilderness, an outpost at war, or while marked.</summary>
    public const string ShowDanger = "danger";

    /// <summary>A HUD setting's value: never shown.</summary>
    public const string ShowOff = "off";

    /// <summary>Screen border value: in a Wilderness as well as while marked.</summary>
    public const string BorderWilderness = "wilderness";

    /// <summary>Screen border value: only while marked.</summary>
    public const string BorderMarked = "marked";

    /// <summary>When the zone pill under the top menu shows: always, danger or off.</summary>
    public static readonly CVarDef<string> Banner =
        CVarDef.Create("wg.wilderness.banner", ShowDanger, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>When the large title shows on crossing into or out of a zone: always, danger or off.</summary>
    public static readonly CVarDef<string> Titles =
        CVarDef.Create("wg.wilderness.titles", ShowDanger, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>When a sound plays on entering a zone or being marked: always, danger or off.</summary>
    public static readonly CVarDef<string> Sounds =
        CVarDef.Create("wg.wilderness.sounds", ShowDanger, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>When the coloured screen border shows: wilderness, marked or off.</summary>
    public static readonly CVarDef<string> ScreenBorder =
        CVarDef.Create("wg.wilderness.screen_border", BorderMarked, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>How strong the screen border is, from 0.1 to 1; 1 is the original strength.</summary>
    public static readonly CVarDef<float> ScreenBorderIntensity =
        CVarDef.Create("wg.wilderness.screen_border_intensity", 0.4f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether the server sends a chat line on entering or leaving a zone. Mark messages are always sent.</summary>
    public static readonly CVarDef<bool> ZoneChat =
        CVarDef.Create("wg.wilderness.zone_chat", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>Whether radars draw zone edges.</summary>
    public static readonly CVarDef<bool> RadarRings =
        CVarDef.Create("wg.wilderness.radar_rings", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
