using Content.Server.Administration;
using Content.Shared._WG.MentorHelp;
using Content.Shared.Administration;
using Robust.Shared.Configuration;
using Robust.Shared.Console;

namespace Content.Server._WG.MentorHelp.Commands;

/// <summary><c>mhelp &lt;question&gt;</c>: opens a ticket, or adds to the player's open one.</summary>
[AnyCommand]
public sealed class MentorHelpCommand : LocalizedEntityCommands
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WGMentorHelpSystem _mentorHelp = default!;

    public override string Command => "mhelp";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        var text = string.Join(' ', args).Trim();
        if (text.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        if (_mentorHelp.GetOpenTicket(player.UserId) != null)
        {
            if (!_mentorHelp.PostAsPlayer(player, text))
                shell.WriteError(Loc.GetString("wg-mhelp-too-long", ("max", WGMentorHelpSystem.MaxLength)));

            return;
        }

        var refusal = _mentorHelp.Open(player, text, null, false) switch
        {
            MentorHelpOpenResult.TooLong => Loc.GetString("wg-mhelp-too-long", ("max", WGMentorHelpSystem.MaxLength)),
            MentorHelpOpenResult.Muted => Loc.GetString("wg-mhelp-muted"),
            MentorHelpOpenResult.Cooldown => Loc.GetString("wg-mhelp-cooldown",
                ("seconds", _cfg.GetCVar(MentorHelpCVars.TicketCooldown))),
            _ => null,
        };

        if (refusal != null)
            shell.WriteError(refusal);
    }
}
