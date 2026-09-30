using Content.Shared._WF.ShipShields;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WfShieldOpen()
    {
        if (_window == null)
            return;
        _window.ShieldEnabledRequested += enabled => SendMessage(new WFShipShieldSetEnabledMessage(enabled));
        _window.ShieldShuntRequested += (direction, concentration, arc) =>
            SendMessage(new WFShipShieldSetShuntMessage(direction, concentration, arc));
    }
}
