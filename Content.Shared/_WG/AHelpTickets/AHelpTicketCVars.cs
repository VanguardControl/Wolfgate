using Robust.Shared.Configuration;

namespace Content.Shared._WG.AHelpTickets;

/// <summary>Cvars for ahelp tickets.</summary>
[CVarDefs]
public sealed class AHelpTicketCVars
{
    /// <summary>Staff get the ticket panel; false brings back the upstream per-player panel.</summary>
    public static readonly CVarDef<bool> TicketsPanel =
        CVarDef.Create("wg.ahelp.tickets_panel", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Minutes an unclaimed ticket waits before staff are reminded of it and it shows as stale; 0 turns that off.</summary>
    public static readonly CVarDef<int> StaleMinutes =
        CVarDef.Create("wg.ahelp.stale_minutes", 5, CVar.SERVER | CVar.REPLICATED);

    /// <summary>How many of a player's past tickets the player card lists.</summary>
    public static readonly CVarDef<int> HistoryLimit =
        CVarDef.Create("wg.ahelp.history_limit", 20, CVar.SERVERONLY);
}
