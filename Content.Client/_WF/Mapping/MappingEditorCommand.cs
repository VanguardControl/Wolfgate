using Content.Client.Gameplay;
using Content.Client.Mapping;
using Robust.Client.State;
using Robust.Shared.Console;

namespace Content.Client._WF.Mapping;

/// <summary>
/// Switches between normal gameplay and the mapping editor screen, which upstream left unreachable.
/// </summary>
public sealed partial class MappingEditorCommand : IConsoleCommand
{
    [Dependency] private IStateManager _state = default!;

    public string Command => "mappingeditor";
    public string Description => "Toggles the mapping editor screen (prototype list, pick, delete, erase).";
    public string Help => "Usage: mappingeditor";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (_state.CurrentState is MappingState)
        {
            _state.RequestStateChange<GameplayState>();
            return;
        }

        if (_state.CurrentState is not GameplayState)
        {
            shell.WriteError("Join the round first.");
            return;
        }

        _state.RequestStateChange<MappingState>();
    }
}
