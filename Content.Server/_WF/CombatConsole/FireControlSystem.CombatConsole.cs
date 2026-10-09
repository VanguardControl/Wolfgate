using Content.Server._WF.CombatConsole;
using Content.Shared._Mono.FireControl;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem
{
    /// <summary>Refreshes the original radar and weapon state after a console setting changes.</summary>
    public void WfRefreshConsole(EntityUid uid) => UpdateUi(uid);

    private void WfUpdateCombatState(EntityUid uid, FireControlConsoleComponent console,
        FireControlConsoleBoundInterfaceState state)
    {
        var system = EntityManager.System<WFCombatConsoleSystem>();
        state.Connected &= system.TryGetServer(uid, console, out _, out _);
        state.Combat = system.GetState(uid, console);
    }
}
