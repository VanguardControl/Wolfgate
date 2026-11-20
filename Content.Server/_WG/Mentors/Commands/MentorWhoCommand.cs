using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.Mentors.Commands;

/// <summary>How many mentors are on duty; mentors and staff also see who.</summary>
[AnyCommand]
public sealed class MentorWhoCommand : LocalizedEntityCommands
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private WGMentorSystem _mentors = default!;

    public override string Command => "mentorwho";

    public override string Help => Description;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine(Loc.GetString("wg-mentor-who", ("count", _mentors.OnDutyCount)));

        var player = shell.Player;
        var mayName = player == null
                      || _mentors.IsMentor(player.UserId)
                      || _admin.HasAdminFlag(player, AdminFlags.Adminhelp);

        if (!mayName || _mentors.OnDutyCount == 0)
            return;

        shell.WriteLine(Loc.GetString("wg-mentor-who-names"));
        foreach (var name in _mentors.OnDutySessions().Select(s => s.Name).Order())
        {
            shell.WriteLine(name);
        }
    }
}
