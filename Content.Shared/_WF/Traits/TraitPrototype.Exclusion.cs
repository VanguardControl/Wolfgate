namespace Content.Shared.Traits;

public sealed partial class TraitPrototype
{
    /// <summary>Shown greyed out, not hidden, in the lobby while a trait it excludes is selected.</summary>
    [DataField]
    public bool GreyOutWhenExcluded { get; private set; }
}
