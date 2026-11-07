using Content.Shared._WF.Blueprints;
using Content.Shared.Research.Components;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Blueprints;

/// <summary>Opens the R&D console window and sends its requests.</summary>
public sealed class WFBlueprintLocatorBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFBlueprintLocatorWindow? _window;

    public WFBlueprintLocatorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WFBlueprintLocatorWindow>();
        _window.OnLocate += patterns => SendMessage(new WFBlueprintLocateMessage(patterns));
        _window.OnAbandon += () => SendMessage(new WFBlueprintAbandonMessage());
        _window.OnServer += () => SendMessage(new ConsoleServerSelectionMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WFBlueprintLocatorState cast)
            _window?.UpdateState(cast);
    }
}
