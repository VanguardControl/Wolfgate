namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>
    /// Lets the character wear hair and facial hair their species can't. Set in the creator; random profiles leave it off.
    /// </summary>
    [DataField]
    public bool MismatchedParts { get; set; }

    public HumanoidCharacterProfile WithMismatchedParts(bool enabled)
    {
        return new(this) { MismatchedParts = enabled };
    }
}
