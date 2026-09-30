using Content.Client._WF.ShipShields;
using Content.Shared._WF.ShipShields;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    private WFShipShieldShuntScreen _shieldScreen = default!;
    /// <summary>The pilot commits a shield allocation.</summary>
    public event Action<float, float, float>? ShieldShuntRequested;

    private void WfShieldInitialize()
    {
        var button = new Button
        {
            Text = Loc.GetString("wf-shield-helm-tab"), ToggleMode = true,
            HorizontalExpand = true, VerticalExpand = true, Group = NavModeButton.Group,
        };
        button.AddStyleClass("ButtonSquare");
        button.OnPressed += _ => SwitchMode(ShuttleConsoleMode.Shields);
        ModeButtons.AddChild(button);
        _shieldScreen = new WFShipShieldShuntScreen { Visible = false };
        _shieldScreen.AllocationRequested += (direction, concentration, arc) =>
            ShieldShuntRequested?.Invoke(direction, concentration, arc);
        Contents.AddChild(_shieldScreen);
    }

    private void WfShieldUpdateState(WFShipShieldShuntState? state, Angle? helmRotation)
    {
        _shieldScreen.UpdateState(state, (float) (helmRotation?.Theta ?? 0));
    }
}
