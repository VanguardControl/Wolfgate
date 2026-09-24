using Content.Shared.Preferences; // WOLFGATE

namespace Content.Shared._Shitmed.Humanoid.Events;

/// <summary>
///     Raised on an entity when their profile has finished being loaded
/// </summary>
// WOLFGATE START: lets anatomy read the loaded profile
// public sealed class ProfileLoadFinishedEvent : EntityEventArgs { }
public sealed class ProfileLoadFinishedEvent : EntityEventArgs
{
    public HumanoidCharacterProfile? Profile;
}
// WOLFGATE END

