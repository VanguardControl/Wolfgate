using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared._WG.Mentors;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Robust.Shared.Configuration;
using Robust.Shared.Console;

namespace Content.Server._WG.Mentors.Commands;

/// <summary><c>mentorban &lt;player&gt; &lt;mentor|help&gt; &lt;minutes or 0&gt; &lt;reason&gt;</c>: a mentor ban or a Mentor Help mute.</summary>
[AdminCommand(AdminFlags.Ban)]
public sealed class MentorBanCommand : LocalizedEntityCommands
{
    [Dependency] private IBanManager _bans = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerLocator _locator = default!;

    public override string Command => "mentorban";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 4 || !uint.TryParse(args[2], out var minutes))
        {
            shell.WriteError(Help);
            return;
        }

        var role = args[1].ToLowerInvariant() switch
        {
            "mentor" => MentorBans.MentorRole,
            "help" => MentorBans.HelpRole,
            _ => null,
        };

        if (role == null)
        {
            shell.WriteError(Help);
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("wg-mentor-cmd-no-player", ("player", args[0])));
            return;
        }

        if (!Enum.TryParse(_cfg.GetCVar(CCVars.RoleBanDefaultSeverity), true, out NoteSeverity severity))
            severity = NoteSeverity.Medium;

        var reason = string.Join(' ', args.Skip(3));
        _bans.CreateRoleBan(located.UserId, located.Username, shell.Player?.UserId, null, located.LastHWId, role,
            minutes, severity, reason, DateTimeOffset.UtcNow);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), "<player>"),
            2 => CompletionResult.FromOptions(new[] { "mentor", "help" }),
            3 => CompletionResult.FromHint("<minutes or 0>"),
            _ => CompletionResult.FromHint("<reason>"),
        };
    }
}
