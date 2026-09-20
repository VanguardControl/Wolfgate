using System.Numerics;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.PlanetCracker.Planets;

/// <summary>
/// Rides a hull through the hop into or out of a world's orbit, so its crew's clients can draw the approach - the
/// planet swelling until it fills the sky, or falling away again - in place of the hyperspace tunnel.
/// It carries the star system rather than the planet: a client builds the very same body from the prototype.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause, UnsavedComponent]
public sealed partial class WFPlanetApproachComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<StarSystemPrototype> System;

    /// <summary>The body's sector position, which is what picks it out of the system's planets.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 PlanetPosition;

    /// <summary>Where the hull was in the sector when the hop began, or where it will be when it ends.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 ShipPosition;

    /// <summary>True on the way in, false on the way out.</summary>
    [DataField, AutoNetworkedField]
    public bool Arriving;

    /// <summary>The travel leg of the hop: the stretch spent on the FTL map, which is all the approach covers.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Start;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan End;
}
