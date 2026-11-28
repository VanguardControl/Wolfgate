using Robust.Shared.GameStates;

namespace Content.Shared._WG.Mentors;

/// <summary>
/// On the body an on-duty mentor is controlling: the examine line, the card sigil and, if they show it, the crown
/// plate over their head.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGMentorMarkComponent : Component
{
    [DataField, AutoNetworkedField]
    public MentorTier Tier;

    /// <summary>Whether the crown plate is drawn over the head (the mentor's own setting).</summary>
    [DataField, AutoNetworkedField]
    public bool ShowCrown = true;
}
