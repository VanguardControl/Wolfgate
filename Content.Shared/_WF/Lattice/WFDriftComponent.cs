using Robust.Shared.GameStates;

namespace Content.Shared._WF.Lattice;

/// <summary>On a ship, or a person off one: how far out of the Lattice's hold it is and how long it has been there.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFDriftComponent : Component
{
    /// <summary>Index of the band it is in, 0 the innermost.</summary>
    [DataField, AutoNetworkedField]
    public int Band;

    /// <summary>Exposure built up out of anchored space. Server only.</summary>
    [DataField]
    public float Drift;

    /// <summary>Drift in steps of <see cref="LevelStep"/>, for clients' screen warping.</summary>
    [DataField, AutoNetworkedField]
    public int Level;

    public const float LevelStep = 5f;

    /// <summary>Depth into its band, 0 to 1, for clients that can't work it out themselves, such as on a planet.</summary>
    [DataField, AutoNetworkedField]
    public float Depth;
}
