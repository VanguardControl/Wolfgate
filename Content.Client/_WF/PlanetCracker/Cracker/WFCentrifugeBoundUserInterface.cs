using Content.Client.Power.PowerCharge;
using Content.Shared.Power;
using Robust.Client.UserInterface;

namespace Content.Client._WF.PlanetCracker.Cracker;

/// <summary>
/// Stands in for PowerChargeBoundUserInterface on the gravitic centrifuge: the same UI key and the same
/// <see cref="SwitchChargingMachineMessage"/>, so no server change and no new key, but it hosts the rotor window
/// instead of the stock charge window.
/// </summary>
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

        // The window is created whether or not PowerChargeComponent has reached the client yet: bailing out here left
        // the interface open with nothing on screen and no retry. The component only supplies the title, and the XAML
        // already carries a locale one.
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
