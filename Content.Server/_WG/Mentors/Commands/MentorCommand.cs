using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared._WG.Mentors;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Network;

namespace Content.Server._WG.Mentors.Commands;

/// <summary>
/// <c>mentor on|off</c> for a mentor's own duty; <c>list</c>, <c>grant</c>, <c>revoke</c>, <c>tier</c> and
/// <c>restore</c> for staff with the Moderator flag.
/// </summary>
[AnyCommand]
public sealed class MentorCommand : LocalizedEntityCommands
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private WGMentorSystem _mentors = default!;

    public override string Command => "mentor";

    private static readonly string[] Subcommands = { "on", "off", "list", "grant", "revoke", "tier", "restore" };

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        switch (args[0])
        {
            case "on":
            case "off":
                Duty(shell, args[0] == "on");
                return;
        }

        if (shell.Player != null && !_admin.HasAdminFlag(shell.Player, AdminFlags.Moderator))
        {
            shell.WriteError(Loc.GetString("shell-missing-required-permission", ("perm", AdminFlags.Moderator)));
            return;
        }

        _ = Staff(shell, args);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromOptions(Subcommands),
            2 when args[0] is "grant" or "revoke" or "tier" or "restore" =>
                CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), "<player>"),
            3 when args[0] is "grant" or "tier" =>
                CompletionResult.FromOptions(new[] { "trainee", "mentor", "senior" }),
            _ => CompletionResult.Empty,
        };
    }

    private void Duty(IConsoleShell shell, bool onDuty)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (!_mentors.SetDuty(player, onDuty))
        {
            shell.WriteError(Loc.GetString("wg-mentor-cmd-not-mentor"));
            return;
        }

        shell.WriteLine(Loc.GetString(onDuty ? "wg-mentor-duty-on" : "wg-mentor-duty-off"));
    }

    private async Task Staff(IConsoleShell shell, string[] args)
    {
        var actor = shell.Player?.UserId;
        switch (args[0])
        {
            case "list":
                await List(shell);
                return;

            case "grant" when args.Length is 2 or 3:
            {
                var tier = MentorTier.Trainee;
                if (args.Length == 3 && !MentorRows.TryParseTier(args[2], out tier))
                    break;

                if (await Locate(shell, args[1]) is not { } user)
                    return;

                await _mentors.GrantAsync(user, tier, actor, MentorRows.SourceStaff);
                shell.WriteLine(Loc.GetString("wg-mentor-cmd-done",
                    ("player", args[1]), ("tier", MentorRows.TierName(tier))));
                return;
            }

            case "revoke" when args.Length >= 3:
            {
                if (await Locate(shell, args[1]) is not { } user)
                    return;

                var reason = string.Join(' ', args.Skip(2));
                if (!await _mentors.RevokeAsync(user, actor, reason))
                {
                    shell.WriteError(Loc.GetString("wg-mentor-cmd-not-on-roster", ("player", args[1])));
                    return;
                }

                shell.WriteLine(Loc.GetString("wg-mentor-cmd-revoked", ("player", args[1])));
                return;
            }

            case "tier" when args.Length == 3:
            {
                if (!MentorRows.TryParseTier(args[2], out var tier))
                    break;

                if (await Locate(shell, args[1]) is not { } user)
                    return;

                if (!await _mentors.SetTierAsync(user, tier, actor))
                {
                    shell.WriteError(Loc.GetString("wg-mentor-cmd-not-on-roster", ("player", args[1])));
                    return;
                }

                shell.WriteLine(Loc.GetString("wg-mentor-cmd-done",
                    ("player", args[1]), ("tier", MentorRows.TierName(tier))));
                return;
            }

            case "restore" when args.Length == 2:
            {
                if (await Locate(shell, args[1]) is not { } user)
                    return;

                if (!await _mentors.RestoreAsync(user, actor))
                {
                    shell.WriteError(Loc.GetString("wg-mentor-cmd-not-restorable", ("player", args[1])));
                    return;
                }

                var tier = _mentors.GetTier(user) ?? MentorTier.Trainee;
                shell.WriteLine(Loc.GetString("wg-mentor-cmd-done",
                    ("player", args[1]), ("tier", MentorRows.TierName(tier))));
                return;
            }
        }

        shell.WriteError(Help);
    }

    private async Task List(IConsoleShell shell)
    {
        var rows = (await _mentors.ListAsync()).Where(MentorRows.OnRoster).ToList();
        if (rows.Count == 0)
        {
            shell.WriteLine(Loc.GetString("wg-mentor-cmd-list-empty"));
            return;
        }

        foreach (var row in rows)
        {
            var located = await _locator.LookupIdAsync(new NetUserId(row.UserId));
            shell.WriteLine(Loc.GetString("wg-mentor-cmd-list-row",
                ("player", located?.Username ?? row.UserId.ToString()),
                ("tier", MentorRows.TierName(MentorRows.TierFromDb(row.Tier))),
                ("state", Loc.GetString($"wg-mentor-state-{row.State}"))));
        }
    }

    private async Task<NetUserId?> Locate(IConsoleShell shell, string name)
    {
        var located = await _locator.LookupIdByNameOrIdAsync(name);
        if (located != null)
            return located.UserId;

        shell.WriteError(Loc.GetString("wg-mentor-cmd-no-player", ("player", name)));
        return null;
    }
}
