using Robust.Shared.Configuration;

namespace Content.Server._WG.ColdStorage;

/// <summary>Server cvars for <see cref="WGGridStasisSystem"/>.</summary>
[CVarDefs]
public sealed class WGStasisCVars
{
    /// <summary>Master switch for grid stasis in space; turning it off wakes every grid at once.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.stasis.enabled", false, CVar.SERVERONLY);

    /// <summary>Seconds a grid must go unattended before it is put in stasis.</summary>
    public static readonly CVarDef<float> Idle =
        CVarDef.Create("wg.stasis.idle", 300f, CVar.SERVERONLY);

    /// <summary>Metres from a grid's bounds within which a viewer keeps an awake grid awake; raised to past the wake range.</summary>
    public static readonly CVarDef<float> Range =
        CVarDef.Create("wg.stasis.range", 128f, CVar.SERVERONLY);

    /// <summary>Metres from a grid's bounds within which a viewer wakes a grid in stasis; raised to past what PVS shows.</summary>
    public static readonly CVarDef<float> WakeRange =
        CVarDef.Create("wg.stasis.wake_range", 96f, CVar.SERVERONLY);

    /// <summary>Milliseconds a tick may spend pausing and waking; a grid someone is about to see wakes whole regardless.</summary>
    public static readonly CVarDef<float> Budget =
        CVarDef.Create("wg.stasis.budget_ms", 2f, CVar.SERVERONLY);

    /// <summary>Whether deeded player ships may enter stasis; one with power running never does.</summary>
    public static readonly CVarDef<bool> PlayerShips =
        CVarDef.Create("wg.stasis.player_ships", false, CVar.SERVERONLY);

    /// <summary>
    /// Whether stations and points of interest may enter stasis while nobody is near or aboard. One with a player,
    /// a body with a mind or a docked attended ship stays awake as any grid does.
    /// </summary>
    public static readonly CVarDef<bool> Stations =
        CVarDef.Create("wg.stasis.stations", false, CVar.SERVERONLY);

    /// <summary>
    /// Whether encounter and crewed NPC ships may enter stasis while no player is near. Their crews stop where
    /// they are and the hull drifts on; encounter timers keep running.
    /// </summary>
    public static readonly CVarDef<bool> NpcShips =
        CVarDef.Create("wg.stasis.npc_ships", false, CVar.SERVERONLY);

    /// <summary>Seconds between sweeps of a grid in stasis for newcomers to pause.</summary>
    public static readonly CVarDef<float> Sweep =
        CVarDef.Create("wg.stasis.sweep", 60f, CVar.SERVERONLY);
}
