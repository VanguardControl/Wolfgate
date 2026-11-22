using Robust.Shared.GameStates;

namespace Content.Shared._WG.MentorCritters;

/// <summary>
/// A mentor critter: a mentor in the round beside the asker as a small animal. Can't be hurt, picked up, eaten or
/// fought; uses only tiny and small things; talks to the asker alone unless speaking aloud.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGMentorCritterComponent : Component
{
    /// <summary>The player the mentor is helping.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Asker;

    /// <summary>The ticket it came from.</summary>
    [DataField]
    public int Ticket;

    /// <summary>Talking in local chat to everyone near, instead of to the asker alone (D7).</summary>
    [DataField, AutoNetworkedField]
    public bool Aloud;

    /// <summary>The animal's name, for the speech and examine text.</summary>
    [DataField, AutoNetworkedField]
    public string Animal = "";

    /// <summary>Set while our own system moves it in or out of a slot, so the unequip guard lets it through.</summary>
    [ViewVariables]
    public bool Moving;
}
