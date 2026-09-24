using Content.Shared.Lathe;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Lathe.UI;

[UsedImplicitly]
public sealed class FabricationSiloBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FabricationSiloMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<FabricationSiloMenu>();
        _menu.OnClientPressed += uid => SendPredictedMessage(new ToggleFabricationSiloClientMessage(uid));
        _menu.OnPartPressed += uid => SendPredictedMessage(new EjectFabricationSiloPartMessage(uid));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is FabricationSiloBuiState silo)
            _menu?.Update(silo);
    }
}
