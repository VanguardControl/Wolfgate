using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.MentorObserve;

/// <summary>The eye a stepped-out mentor watches the asker through.</summary>
[RegisterComponent]
public sealed partial class WGMentorObserverComponent : Component
{
}

/// <summary>
/// On a mentor's body while they are away watching or as a critter: an examine line, and back to the body if it is hurt.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGMentorAwayComponent : Component
{
}

/// <summary>On a ghost mentor bound to the asker; moving away ends the watch.</summary>
[RegisterComponent]
public sealed partial class WGMentorWatcherComponent : Component
{
    [DataField]
    public EntityUid Asker;
}

/// <summary>Return to body: ends a step-out or a critter visit.</summary>
public sealed partial class WGMentorReturnActionEvent : InstantActionEvent
{
}
