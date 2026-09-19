using Content.Shared._WF.PlanetCracker.Survey.BUI;
using Robust.Client.UserInterface;

namespace Content.Client._WF.PlanetCracker.Survey;

/// <summary>
/// Hosts <see cref="WFSurveyConsoleWindow"/>. It sends NOTHING: the survey console is a read-only instrument, because
/// the "go here" is each body's own pre-existing FTL beacon name - StarSystemMapSystem spawns one FTLBeacon-carrying
/// PlanetEntity per star-system entry and renames it to the planet's name
/// (Content.Server/_FarHorizons/StarSystem/StarSystemMapSystem.cs:57-66), and that is exactly the label
/// ShuttleConsoleSystem.GetBeacons puts in the pilot's destination tree
/// (Content.Server/Shuttles/Systems/ShuttleConsoleSystem.FTL.cs:93-109) - so there is nothing for the client to ask
/// the server to do. Row selection is a client-local highlight and stays in the window.
/// </summary>
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
