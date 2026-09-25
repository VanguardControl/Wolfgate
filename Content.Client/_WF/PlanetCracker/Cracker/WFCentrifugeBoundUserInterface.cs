using Content.Client.Power.PowerCharge;
using Content.Shared.Power;
using Robust.Client.UserInterface;

namespace Content.Client._WF.PlanetCracker.Cracker;

/// <summary>Gravitic centrifuge UI: the stock power-charge key and messages, hosting the rotor window.</summary>
public sealed class WFCentrifugeBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFCentrifugeWindow? _window;

    public WFCentrifugeBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    /// <summary>Flips the machine's power switch, exactly as the stock window does.</summary>
    public void SetPowerSwitch(bool on)
    {
        SendMessage(new SwitchChargingMachineMessage(on));
    }

    /// <inheritdoc/>
    protected override void Open()
    {
        base.Open();

        // Open even before PowerChargeComponent arrives; it only supplies the title.
        var title = EntMan.TryGetComponent(Owner, out PowerChargeComponent? charge)
            ? Loc.GetString(charge.WindowTitle)
            : null;

        _window = this.CreateWindow<WFCentrifugeWindow>();
        _window.SetOwner(this, title);
    }

    /// <inheritdoc/>
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is PowerChargeState chargeState)
            _window?.UpdateState(chargeState);
    }
}
