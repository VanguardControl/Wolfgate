using Robust.Shared.GameStates;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>
/// Mapper-placed marker on the cracker grid naming where a cut chunk is berthed; the centre is Distance tiles along the marker's facing.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFChunkBerthComponent : Component
{
    /// <summary>Berth rectangle in tiles; design section 5 wants at least 48x48 on the real hull.</summary>
    [DataField, AutoNetworkedField]
    public Vector2i Size = new(48, 48);

    /// <summary>Tiles from the marker to the berth centre, along the marker's own facing.</summary>
    [DataField, AutoNetworkedField]
    public float Distance = 26f;
}
