using Content.Server.Administration.Logs;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Logs)]
public sealed class OpenAdminLogsCommand : IConsoleCommand
{
    public string Command => "adminlogs";
    public string Description => "Opens the admin logs panel.";
    public string Help => $"Usage: {Command}";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        // WOLFGATE(LogInvestigator) START: the investigator replaces the log window while it is on
        if (Content.Server._WG.LogInvestigator.LogInvestigatorEui.TryOpen(player, null))
            return;
        // WOLFGATE END

        var eui = IoCManager.Resolve<EuiManager>();
        var ui = new AdminLogsEui();
        eui.OpenEui(ui, player);
    }
}
