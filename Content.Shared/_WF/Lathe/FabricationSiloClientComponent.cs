namespace Content.Shared._WF.Lathe;

/// <summary>
/// Links a lathe to one parts silo and one chemical silo.
/// </summary>
[RegisterComponent]
public sealed partial class FabricationSiloClientComponent : Component
{
    /// <summary>
    /// Linked parts silo.
    /// </summary>
    [DataField]
    public EntityUid? PartsSilo;

    /// <summary>
    /// Linked chemical silo.
    /// </summary>
    [DataField]
    public EntityUid? ChemicalSilo;
}
