using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.Mentors.Commands;

/// <summary><c>msay &lt;text&gt;</c>: mentor chat. Who may use it is checked when the message is sent.</summary>
[AnyCommand]
public sealed class MentorSayCommand : LocalizedCommands
{
    [Dependency] private IChatManager _chat = default!;

    public override string Command => "msay";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        var message = string.Join(" ", args).Trim();
        if (string.IsNullOrEmpty(message))
            return;

        _chat.TrySendOOCMessage(player, message, OOCChatType.Mentor);
    }
}
