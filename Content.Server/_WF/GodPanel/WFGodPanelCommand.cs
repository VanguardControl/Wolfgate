using Content.Server.Administration;
using Content.Shared._WF.GodPanel;
using Content.Shared.Administration;
using Robust.Server.Console;
using Robust.Shared.Console;

namespace Content.Server._WF.GodPanel;

/// <summary>
/// Opens the God Panel for the calling admin. Also gates the Wolfgate tab button.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFGodPanelCommand : LocalizedEntityCommands
{
    [Dependency] private IServerConsoleHost _console = default!;

    public override string Command => WFGodPanelCommands.GodPanel;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-godpanel-no-player"));
            return;
        }

        _console.RemoteExecuteCommand(player, WFGodPanelCommands.GodPanelUi);
    }
}
