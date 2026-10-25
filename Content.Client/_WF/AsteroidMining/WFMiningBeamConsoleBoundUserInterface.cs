using Content.Shared._WF.AsteroidMining;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WF.AsteroidMining;

[UsedImplicitly]
public sealed class WFMiningBeamConsoleBoundUserInterface : BoundUserInterface
{
    private WFMiningBeamConsoleWindow? _window;

    public WFMiningBeamConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFMiningBeamConsoleWindow>();
        _window.OnPower += (emitter, power) => SendMessage(new WFMiningBeamPowerMessage(emitter, power));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WFMiningBeamConsoleBoundUserInterfaceState miningState)
            _window?.UpdateState(miningState);
    }
}
