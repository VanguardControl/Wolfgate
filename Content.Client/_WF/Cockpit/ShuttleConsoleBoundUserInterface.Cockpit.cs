using Content.Shared._WF.Cockpit;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WfCockpitGunneryOpen()
    {
        if (_window != null)
            _window.WfCockpitGunneryCommand += SendMessage;
    }

    private void WfCockpitGunneryReceive(BoundUserInterfaceMessage message)
    {
        if (message is WFCockpitGunneryStateMessage state)
            _window?.WfReceiveCockpitGunnery(state);
    }
}
