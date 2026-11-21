using Content.Server._WG.ColdStorage;

namespace Content.Server._Mono.Cleanup;

/// <summary>Space cleanup's protection radii shrink to half at full entity pressure.</summary>
public sealed partial class SpaceCleanupSystem
{
    [Dependency] private WGEntityPressureSystem _wgPressure = default!;

    /// <summary>The aggression that scales every radius, tightened by pressure; unchanged at pressure 0.</summary>
    internal float WGPressureAggression(float aggression) => aggression * _wgPressure.Scale(1f, 0.5f);
}

/// <summary>Grid cleanup's player distance shrinks to half, and its delay to half, at full entity pressure.</summary>
public sealed partial class GridCleanupSystem
{
    [Dependency] private WGEntityPressureSystem _wgPressure = default!;

    /// <summary>Factor on the player distance.</summary>
    internal float WGRadiusScale => _wgPressure.Scale(1f, 0.5f);

    /// <summary>Factor on how long a grid must be a candidate before it goes.</summary>
    internal float WGDurationScale => _wgPressure.Scale(1f, 0.5f);
}

/// <summary>Mob cleanup's protection radii shrink to half at full entity pressure.</summary>
public sealed partial class MobCleanupSystem
{
    [Dependency] private WGEntityPressureSystem _wgPressure = default!;

    /// <summary>Factor on both protection radii.</summary>
    internal float WGRadiusScale => _wgPressure.Scale(1f, 0.5f);
}
