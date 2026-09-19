using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>Appearance keys the sector survey console writes, driving its GenericVisualizer screen layer.</summary>
[Serializable, NetSerializable]
public enum WFSurveyConsoleVisuals : byte
{
    /// <summary>The console's current <see cref="WFSurveyConsoleScreen"/>.</summary>
    Screen,
}

/// <summary>Screen faces survey_console.rsi ships - exactly these two, nothing else.</summary>
[Serializable, NetSerializable]
public enum WFSurveyConsoleScreen : byte
{
    /// <summary>Nobody has the interface open.</summary>
    Idle,

    /// <summary>At least one listener has the interface open and is being pushed state.</summary>
    Scanning,
}
