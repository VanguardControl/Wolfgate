using Content.Shared._WG.Biotype;

namespace Content.Shared.Humanoid.Prototypes;

public sealed partial class SpeciesPrototype
{
    /// <summary>The species' native biotype: what its own body and components are.</summary>
    [DataField]
    public WGBiotype Biotype { get; private set; } = WGBiotype.Organic;

    /// <summary>The species can only be its native biotype; a stored other one is ignored and the creator won't offer it.</summary>
    [DataField]
    public bool BiotypeLocked { get; private set; }
}
