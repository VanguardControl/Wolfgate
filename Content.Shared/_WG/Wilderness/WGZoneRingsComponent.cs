using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Wilderness;

/// <summary>Every zone's edge on a map, for radars: maps reach every client, far grids may not.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGZoneRingsComponent : Component
{
    [AutoNetworkedField]
    public List<WGZoneRing> Rings = new();
}

/// <summary>One zone's edge: a circle on the map.</summary>
[Serializable, NetSerializable]
public readonly record struct WGZoneRing(Vector2 Centre, float Radius, ProtoId<WGZonePrototype> Zone);
