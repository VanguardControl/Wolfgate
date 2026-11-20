using Robust.Shared.Configuration;

namespace Content.Shared._WG.MentorHelp;

/// <summary>Settings for Mentor Help tickets.</summary>
[CVarDefs]
public sealed class MentorHelpCVars
{
    /// <summary>Seconds a player waits between opening two tickets.</summary>
    public static readonly CVarDef<int> TicketCooldown =
        CVarDef.Create("wg.mentor.ticket_cooldown", 30, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Minutes an unclaimed ticket waits before it is offered to staff as a Gameplay ahelp.</summary>
    public static readonly CVarDef<int> FallbackMinutes =
        CVarDef.Create("wg.mentor.fallback_minutes", 5, CVar.SERVERONLY);

    /// <summary>Minutes without a message after which a ticket closes.</summary>
    public static readonly CVarDef<int> IdleCloseMinutes =
        CVarDef.Create("wg.mentor.idle_close_minutes", 20, CVar.SERVERONLY);

    /// <summary>Discord webhook that gets a one-way copy of ticket lines. Empty turns it off.</summary>
    public static readonly CVarDef<string> DiscordWebhook =
        CVarDef.Create("wg.mentor.discord_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>Days a ticket's transcript is kept before its text is deleted.</summary>
    public static readonly CVarDef<int> TranscriptDays =
        CVarDef.Create("wg.mentor.transcript_days", 180, CVar.SERVERONLY);
}
