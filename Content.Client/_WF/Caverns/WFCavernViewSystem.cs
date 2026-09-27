using System.Diagnostics.CodeAnalysis;
using Content.Client._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Caverns;

namespace Content.Client._WF.Caverns;

/// <summary>
/// What the z-level renderer needs to show a cavern through the holes in the ground above it: the cavern, whether a
/// hole is in view, and where the sky must stay out.
/// </summary>
public sealed partial class WFCavernViewSystem : EntitySystem
{
    [Dependency] private CEClientZLevelsSystem _zLevels = default!;
    [Dependency] private WFCavernShadeVisualsSystem _shades = default!;

    /// <summary>The cavern under a ground layer, once this client has its map; what is on it streams in with its eye.</summary>
    public bool TryGetCavernBelow(EntityUid ground, [NotNullWhen(true)] out EntityUid? cavern)
    {
        cavern = null;

        if (!HasComp<CEZGroundLayerComponent>(ground)
            || !_zLevels.TryMapOffset(ground, -1, out var below)
            || !HasComp<WFCavernLayerComponent>(below))
            return false;

        cavern = below.Owner;
        return true;
    }

    /// <summary>
    /// Whether a map's pass must not draw the sky in this view: a cavern never does, and a ground doesn't while one of
    /// its holes is in view, so a hole shows the cavern under it or, before it streams in, plain dark.
    /// </summary>
    public bool HidesSky(EntityUid map, Box2 worldBox)
    {
        return HasComp<WFCavernLayerComponent>(map) || AnyHoleWithin(map, worldBox);
    }

    /// <summary>Whether one of a ground's cavern mouths has a hole tile inside a world box.</summary>
    public bool AnyHoleWithin(EntityUid ground, Box2 worldBox)
    {
        return _shades.AnyPitWithin(ground, worldBox);
    }

    /// <summary>
    /// The depth of the cavern pass under a ground layer drawn at this depth: one level down, when this client has the
    /// cavern and one of its mouths is in view; otherwise null, and the ground stays the floor of the view.
    /// </summary>
    public static float? CavernPassDepth(float groundDepth, bool cavernKnown, bool mouthInView)
    {
        return cavernKnown && mouthInView ? groundDepth - 1f : null;
    }
}
