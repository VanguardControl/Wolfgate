using Content.Client.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Console;
using Robust.Shared.Input.Binding;

namespace Content.Client._WF.Mapping;

/// <summary>
/// Ctrl+Z and Ctrl+Y on the server's mapping undo commands. Lives on a system rather than a state controller so
/// the keys work the same in normal gameplay and in the mapping editor screen.
/// </summary>
public sealed partial class MappingUndoInputSystem : EntitySystem
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IConsoleHost _console = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.MappingUndo, InputCmdHandler.FromDelegate(_ => Run("mapundo")))
            .Bind(ContentKeyFunctions.MappingRedo, InputCmdHandler.FromDelegate(_ => Run("mapredo")))
            .Register<MappingUndoInputSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<MappingUndoInputSystem>();
        base.Shutdown();
    }

    /// <summary>
    /// Sent straight to the server: the command only exists there, and the server checks the mapper's flags
    /// again anyway. Nothing happens while a text box has the keyboard, so Ctrl+Z still edits chat.
    /// </summary>
    private void Run(string command)
    {
        if (_ui.KeyboardFocused != null || _ui.ControlFocused != null)
            return;

        if (!_admin.HasFlag(AdminFlags.Mapping) && !_admin.HasFlag(AdminFlags.Host))
            return;

        _console.RemoteExecuteCommand(null, command);
    }
}
