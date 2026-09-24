using Content.Shared._WF.Lathe;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Lathe.UI;

/// <summary>
/// Opens the fabrication silo window and sends its link and eject requests.
/// </summary>
[UsedImplicitly]
public sealed class FabricationSiloBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FabricationSiloMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<FabricationSiloMenu>();
        _menu.OnClientPressed += uid => SendMessage(new ToggleFabricationSiloClientMessage(uid));
        _menu.OnPartPressed += uid => SendMessage(new EjectFabricationSiloPartMessage(uid));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is FabricationSiloBuiState silo)
            _menu?.Update(silo);
    }
}
