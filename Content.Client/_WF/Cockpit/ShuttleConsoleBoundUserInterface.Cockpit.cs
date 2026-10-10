using Content.Shared._WF.Cockpit;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WfCockpitGunneryOpen()
    {
        if (_window != null)
            // Mode changes and their first command must retain order within the same input tick.
            _window.WfCockpitGunneryCommand += SendPredictedMessage;
    }

    private void WfCockpitGunneryReceive(BoundUserInterfaceMessage message)
    {
        if (message is WFCockpitGunneryStateMessage state)
            _window?.WfReceiveCockpitGunnery(state);
    }
}
