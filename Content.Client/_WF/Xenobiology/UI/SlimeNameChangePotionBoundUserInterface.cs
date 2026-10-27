using Content.Shared._WF.Xenobiology.Potions;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Xenobiology.UI;

/// <summary>
/// Sends the name typed into the name change potion's window.
/// </summary>
public sealed class SlimeNameChangePotionBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private SlimeNameChangePotionWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<SlimeNameChangePotionWindow>();
        _window.OnNameChanged += OnNameChanged;

        if (EntMan.TryGetComponent(Owner, out SlimeNameChangePotionComponent? comp))
            _window.SetName(comp.AssignedName);
    }

    private void OnNameChanged(string name)
    {
        if (EntMan.TryGetComponent(Owner, out SlimeNameChangePotionComponent? comp) && comp.AssignedName == name)
            return;

        SendPredictedMessage(new SlimeNameChangePotionNewNameChangedMessage(name));
    }
}
