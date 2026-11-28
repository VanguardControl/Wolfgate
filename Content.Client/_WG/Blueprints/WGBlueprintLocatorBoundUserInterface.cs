using Content.Shared._WG.Blueprints;
using Content.Shared.Research.Components;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Blueprints;

/// <summary>Opens the R&D console window and sends its requests.</summary>
public sealed class WGBlueprintLocatorBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WGBlueprintLocatorWindow? _window;

    public WGBlueprintLocatorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WGBlueprintLocatorWindow>();
        _window.OnLocate += patterns => SendMessage(new WGBlueprintLocateMessage(patterns));
        _window.OnAbandon += () => SendMessage(new WGBlueprintAbandonMessage());
        _window.OnServer += () => SendMessage(new ConsoleServerSelectionMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WGBlueprintLocatorState cast)
            _window?.UpdateState(cast);
    }
}
