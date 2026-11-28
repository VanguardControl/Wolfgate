using Content.Shared.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._Mono.Cleanup;

/// <summary>Grid cleanup's verdict on a grid about to be stored, so its stand-in can keep the same clock.</summary>
public sealed partial class GridCleanupSystem
{
    /// <summary>How long a grid must stay a candidate before it goes, at the current entity pressure.</summary>
    internal TimeSpan WGCleanupDuration => _duration * WGDurationScale;

    /// <summary>
    /// Whether grid cleanup would be counting down on a grid as it stands, players left out, with its clock so far,
    /// the seconds the clock gains per second and the player distance that resets it (before pressure scaling, which
    /// a grid's own override never gets).
    /// </summary>
    internal bool WGColdStoreCandidate(EntityUid uid, out TimeSpan accumulator, out float rate, out float distance, out bool distanceFixed)
    {
        accumulator = TimeSpan.Zero;
        rate = 0f;
        distance = 0f;
        distanceFixed = false;
        if (!TryComp<MapGridComponent>(uid, out _) || !TryComp<PhysicsComponent>(uid, out var body))
            return false;

        var xform = Transform(uid);
        var state = EnsureComp<GridCleanupGridComponent>(uid);
        accumulator = state.CleanupAccumulator;
        var tiles = body.FixturesMass / ShuttleSystem.TileDensityMultiplier;
        var scale = MathF.Max(MathF.Min(tiles / _aggressiveTiles, 1f), 0.01f);
        rate = state.CleanupAcceleration / scale;
        distance = state.DistanceOverride ?? _maxDistance * scale * scale;
        distanceFixed = state.DistanceOverride != null;

        // The same tests as ShouldEntityCleanup, players excepted; the price is that of the grid as it is stored.
        return !HasComp<MapComponent>(uid)
               && !HasComp<MapGridComponent>(xform.ParentUid)
               && !_immuneQuery.HasComp(uid)
               && !(!state.IgnoreIFF && TryComp<IFFComponent>(uid, out var iff) && (iff.Flags & IFFFlags.HideLabel) == 0)
               && !(!state.IgnorePowered && HasPoweredAPC((uid, xform)))
               && !(!state.IgnorePrice && _pricing.AppraiseGrid(uid) > _maxValue);
    }
}
