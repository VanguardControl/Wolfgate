using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.Wolfmed.Commands;

/// <summary>
/// TEMPORARY (playtest 5): "I don't want to leave a possibly game-breaking bug over night." Any player can heal
/// themselves fully; the price is a ticket to the admins (and Discord) with what broke and the state they were in,
/// and misuse is a ban. Remove with <see cref="WolfmedBugRescueSystem"/> once the playtest is over.
/// </summary>
[AnyCommand]
public sealed class HealMeImBrokenCommand : LocalizedEntityCommands
{
    [Dependency] private WolfmedBugRescueSystem _rescue = default!;

    public override string Command => "healmeimbroken";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (!_rescue.TryBegin(player, out var refusal))
            shell.WriteError(refusal);
    }
}
