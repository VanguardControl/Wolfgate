using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Wilderness;

/// <summary>The zone a player is in now, sent only to that player. Removed outside every zone.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGInZoneComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<WGZonePrototype> Zone;

    /// <summary>Whether the war was HOT when the zone was last looked at.</summary>
    [DataField, AutoNetworkedField]
    public bool Hot;

    /// <summary>The name of the place the zone is round, such as the station's.</summary>
    [DataField, AutoNetworkedField]
    public string Place = string.Empty;

    public override bool SendOnlyToOwner => true;
}
