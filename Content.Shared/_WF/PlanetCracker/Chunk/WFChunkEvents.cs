using System.Numerics;

namespace Content.Shared._WF.PlanetCracker.Chunk;

/// <summary>Raised broadcast once a disc has been cut, moved into the berth and parked; the F6 hook.</summary>
[ByRefEvent]
public readonly record struct WFChunkExtractedEvent(
    EntityUid Chunk,
    EntityUid Cracker,
    EntityUid GroundMap,
    Vector2 HoleCentre,
    float Radius);

/// <summary>Raised broadcast as a chunk is pushed into transit, whether by the watchdog or by the hull's own fall; the F7 hook.</summary>
[ByRefEvent]
public readonly record struct WFChunkDroppedEvent(EntityUid Chunk, EntityUid Cracker);

/// <summary>
/// Raised broadcast once a dropped chunk has left its transit map and settled on the ground layer.
/// Cracker and GroundMap may be EntityUid.Invalid when the back-link or the recorded map no longer resolves.
/// </summary>
[ByRefEvent]
public readonly record struct WFChunkLandedEvent(EntityUid Chunk, EntityUid Cracker, EntityUid GroundMap);

/// <summary>Raised broadcast when a sector body is flagged Cracked, so a planet can only ever be cut once; the F9 hook.</summary>
[ByRefEvent]
public readonly record struct WFPlanetCrackedEvent(EntityUid Planet, EntityUid Chunk, EntityUid Cracker);
