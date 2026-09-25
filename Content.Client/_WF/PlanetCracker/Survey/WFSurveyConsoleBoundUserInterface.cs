using Content.Shared._WF.PlanetCracker.Survey.BUI;
using Robust.Client.UserInterface;

namespace Content.Client._WF.PlanetCracker.Survey;

/// <summary>Hosts <see cref="WFSurveyConsoleWindow"/>; read-only, since each planet's FTL beacon already carries its name.</summary>
public sealed class WFSurveyConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFSurveyConsoleWindow? _window;

    public WFSurveyConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    /// <inheritdoc/>
    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WFSurveyConsoleWindow>();
    }

    /// <inheritdoc/>
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WFSurveyConsoleState surveyState)
            _window?.UpdateState(surveyState);
    }
}
