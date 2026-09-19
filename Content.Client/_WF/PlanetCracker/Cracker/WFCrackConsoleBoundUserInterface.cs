using Content.Shared._WF.PlanetCracker.Cracker.BUI;
using Robust.Client.UserInterface;

namespace Content.Client._WF.PlanetCracker.Cracker;

/// <summary>
/// Hosts <see cref="WFCrackConsoleWindow"/> and carries the three crack messages. There is deliberately no abort
/// message: once the cut has begun it cannot be called off (design D23).
/// </summary>
public sealed class WFCrackConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFCrackConsoleWindow? _window;

    public WFCrackConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    /// <inheritdoc/>
    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WFCrackConsoleWindow>();
        _window.OnTargetPressed += () => SendMessage(new WFCrackTargetMessage());
        _window.OnUntargetPressed += () => SendMessage(new WFCrackUntargetMessage());
        _window.OnBeginPressed += () => SendMessage(new WFCrackBeginMessage());
    }

    /// <inheritdoc/>
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WFCrackConsoleState crackState)
            _window?.UpdateState(crackState);
    }
}
