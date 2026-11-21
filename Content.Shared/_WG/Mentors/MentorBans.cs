namespace Content.Shared._WG.Mentors;

/// <summary>
/// The role ban strings for mentors (D5). They are stored as they are in <c>server_role_ban</c>, beside the
/// <c>Job:</c> bans, and never take the job prefix.
/// </summary>
public static class MentorBans
{
    public const string Prefix = "Mentor:";

    /// <summary>Banned from being a mentor: off the roster for its length, and can't apply.</summary>
    public const string MentorRole = "Mentor:Mentor";

    /// <summary>Muted in Mentor Help: can't open tickets, keeps the admin help.</summary>
    public const string HelpRole = "Mentor:Help";

    public static readonly string[] Roles = { MentorRole, HelpRole };

    public static bool IsRole(string role)
    {
        return role is MentorRole or HelpRole;
    }
}
