using Content.Client._WF.GodPanel.UI;
using Content.Shared._WF.GodPanel;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Client._WF.GodPanel;

/// <summary>
/// Opens the God Panel window. Sent by the server's godpanel command, which does the permission check.
/// </summary>
[AnyCommand]
public sealed class WFGodPanelUiCommand : LocalizedCommands
{
    private WFGodPanelWindow? _window;

    public override string Command => WFGodPanelCommands.GodPanelUi;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (_window is { Disposed: false, IsOpen: true })
        {
            _window.MoveToFront();
            return;
        }

        _window = new WFGodPanelWindow();
        _window.OpenCentered();
    }
}
