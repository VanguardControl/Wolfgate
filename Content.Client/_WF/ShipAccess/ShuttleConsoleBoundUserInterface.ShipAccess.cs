using Content.Shared._WF.ShipAccess;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WfAccessOpen()
    {
        if (_window == null)
            return;

        _window.ShipAccessLockedRequested += locked => SendMessage(new WFShipAccessSetLockedMessage(locked));
        _window.ShipAccessAddRequested += target => SendMessage(new WFShipAccessAddPlayerMessage(target));
        _window.ShipAccessRemoveRequested += userId => SendMessage(new WFShipAccessRemoveMessage(userId));
        _window.ShipAccessBuilderRequested += (userId, builder) => SendMessage(new WFShipAccessSetBuilderMessage(userId, builder));
        _window.ShipAccessClaimRequested += () => SendMessage(new WFShipAccessClaimMessage());
        _window.ShipAccessDoorRuleRequested += (door, rule) => SendMessage(new WFShipAccessSetDoorRuleMessage(door, rule));
        _window.ShipAccessDoorPlayerRequested += (door, userId, listed) => SendMessage(new WFShipAccessSetDoorPlayerMessage(door, userId, listed));
    }
}
