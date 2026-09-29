namespace Content.Shared.Humanoid;

public sealed partial class HumanoidAppearanceComponent
{
    /// <summary>
    /// Loaded from a profile with Mismatched parts on: its hair and facial hair draw even where the species has no layer
    /// for them.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool MismatchedParts;
}
