using Content.Shared.Atmos;

namespace Content.Server._WG.ColdStorage;

/// <summary>
/// The gas a pipe held when its grid was stored. Pipe nets are rebuilt on load and start empty, so each pipe carries
/// its share through the save and gives it back to the net it lands in.
/// </summary>
[RegisterComponent]
public sealed partial class WGKeptPipeGasComponent : Component
{
    /// <summary>Gas by node name.</summary>
    [DataField]
    public Dictionary<string, GasMixture> Nodes = new();

    /// <summary>Set on the grid being saved, never written: only a loaded copy gives its gas back.</summary>
    public bool Saving;
}
