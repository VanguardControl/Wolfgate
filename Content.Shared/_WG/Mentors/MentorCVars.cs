using Robust.Shared.Configuration;

namespace Content.Shared._WG.Mentors;

/// <summary>Settings for the mentor roster, applications, promotion and inactivity.</summary>
[CVarDefs]
public sealed class MentorCVars
{
    /// <summary>Master switch for mentors and Mentor Help.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.mentor.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Overall playtime in hours a player needs to apply.</summary>
    public static readonly CVarDef<float> MinHours =
        CVarDef.Create("wg.mentor.min_hours", 100f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>How applications are approved: auto, review, or hybrid (auto on a spotless record, else review).</summary>
    public static readonly CVarDef<string> ApplicationMode =
        CVarDef.Create("wg.mentor.application_mode", "hybrid", CVar.SERVERONLY);

    /// <summary>Days back the clean-record check looks for bans and notes.</summary>
    public static readonly CVarDef<int> CleanDays =
        CVarDef.Create("wg.mentor.clean_days", 90, CVar.SERVERONLY);

    /// <summary>Days before a player may apply again after a denial, revocation or resignation.</summary>
    public static readonly CVarDef<int> ReapplyDays =
        CVarDef.Create("wg.mentor.reapply_days", 30, CVar.SERVERONLY);

    /// <summary>Resolved tickets a Trainee needs for promotion to Mentor.</summary>
    public static readonly CVarDef<int> PromoteTickets =
        CVarDef.Create("wg.mentor.promote_tickets", 10, CVar.SERVERONLY);

    /// <summary>Share of rated tickets, 0 to 1, that must be positive for promotion.</summary>
    public static readonly CVarDef<float> PromoteRating =
        CVarDef.Create("wg.mentor.promote_rating", 0.8f, CVar.SERVERONLY);

    /// <summary>Days on the roster before a Trainee can be promoted.</summary>
    public static readonly CVarDef<int> PromoteDays =
        CVarDef.Create("wg.mentor.promote_days", 14, CVar.SERVERONLY);

    /// <summary>Days without logging in after which a mentor goes inactive.</summary>
    public static readonly CVarDef<int> InactiveDays =
        CVarDef.Create("wg.mentor.inactive_days", 90, CVar.SERVERONLY);

    /// <summary>Whether this mentor shows the crown plate over their head while on duty.</summary>
    public static readonly CVarDef<bool> ShowCrown =
        CVarDef.Create("wg.mentor.show_crown", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>Set once this player was told they have the hours to apply, so the notice comes once.</summary>
    public static readonly CVarDef<bool> EligibleNoticed =
        CVarDef.Create("wg.mentor.eligible_noticed", false, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>Whether this mentor starts every connection off duty.</summary>
    public static readonly CVarDef<bool> StartOffDuty =
        CVarDef.Create("wg.mentor.start_off_duty", false, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);
}
