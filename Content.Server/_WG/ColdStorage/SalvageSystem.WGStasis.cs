using Content.Server._WG.ColdStorage;

namespace Content.Server.Salvage;

/// <summary>Keeps the grids of a live salvage magnet out of stasis.</summary>
public sealed partial class SalvageSystem
{
    [Dependency] private WGGridStasisSystem _wgStasis = default!;

    /// <summary>The grid each magnet's station-side data pinned when it took an offer.</summary>
    private readonly Dictionary<EntityUid, EntityUid> _wgMagnetGrids = new();

    private static string WGMagnetPin(EntityUid data) => $"salvage-magnet {data}";

    /// <summary>Pins the salvage a magnet pulled in; deleting the grid at the end of the lease drops the pin.</summary>
    private void WGPinSalvage(EntityUid data, EntityUid salvage)
    {
        _wgStasis.Pin(salvage, WGMagnetPin(data));
    }

    /// <summary>Pins the grid the magnet sits on until the lease ends.</summary>
    private void WGPinMagnetGrid(EntityUid data, EntityUid? grid)
    {
        if (grid is not { } pinned)
            return;

        WGUnpinMagnetGrid(data);
        _wgMagnetGrids[data] = pinned;
        _wgStasis.Pin(pinned, WGMagnetPin(data));
    }

    private void WGUnpinMagnetGrid(EntityUid data)
    {
        if (_wgMagnetGrids.Remove(data, out var grid))
            _wgStasis.Unpin(grid, WGMagnetPin(data));
    }
}
