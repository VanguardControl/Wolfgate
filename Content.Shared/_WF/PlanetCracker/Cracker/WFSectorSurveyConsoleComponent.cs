using Robust.Shared.GameStates;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>The console that surveys sector bodies for crackable ground; a shell until the F4 interface exists.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFSectorSurveyConsoleComponent : Component;
