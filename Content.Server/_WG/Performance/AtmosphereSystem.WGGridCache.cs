using Content.Server.Atmos.Components;
using Content.Shared.Atmos.Components;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>A full rebuild this often catches an atmosphere whose parts arrived in an unusual order.</summary>
    private static readonly TimeSpan WGGridCacheRebuild = TimeSpan.FromSeconds(5);

    private readonly List<Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent>> _wgAtmospheres = new();
    private bool _wgAtmospheresDirty = true;
    private TimeSpan _wgNextAtmospheresRebuild;

    /// <summary>Marks the grid atmosphere list for a rebuild before the next run.</summary>
    private void WGAtmospheresChanged() => _wgAtmospheresDirty = true;

    /// <summary>Fills a new run with every grid atmosphere, walking the entities only after one was added or removed.</summary>
    private void WGQueueRunAtmospheres()
    {
        var now = _gameTiming.RealTime;
        if (_wgAtmospheresDirty || now >= _wgNextAtmospheresRebuild)
        {
            _wgAtmospheres.Clear();
            var query = EntityQueryEnumerator<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var atmos, out var overlay, out var grid, out var xform))
            {
                _wgAtmospheres.Add((uid, atmos, overlay, grid, xform));
            }

            _wgAtmospheresDirty = false;
            _wgNextAtmospheresRebuild = now + WGGridCacheRebuild;
        }

        _currentRunAtmosphere.AddRange(_wgAtmospheres);
    }
}
