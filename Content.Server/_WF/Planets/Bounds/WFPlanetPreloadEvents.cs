using System.Numerics;
using System.Threading.Tasks;

namespace Content.Server._WF.Planets.Bounds;

/// <summary>
/// Raised broadcast the tick before a bounded ground starts generating its whole circle, so other modules can place
/// what must go in before the terrain exists, such as cavern mouths. Work that takes more than the tick, such as a
/// dungeon, goes in <see cref="Holds"/>: no chunk loads until every hold has finished.
/// </summary>
[ByRefEvent]
public readonly record struct WFPlanetPreloadStartingEvent(EntityUid Ground, Vector2 Centre, float Radius, List<Task> Holds);

/// <summary>Raised broadcast once a ground's whole circle is generated and its boundary ring laid.</summary>
[ByRefEvent]
public readonly record struct WFPlanetPreloadedEvent(EntityUid Ground);

/// <summary>On a ground layer whose whole circle is generated: nothing on it streams in or unloads any more.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetPreloadedComponent : Component;
