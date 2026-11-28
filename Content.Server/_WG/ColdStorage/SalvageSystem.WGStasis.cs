using Content.Server._WG.ColdStorage;
using Content.Server.Salvage.Magnet;

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

    /// <summary>
    /// Whether the lease an offer was taken for is still the magnet's. Generation is awaited, and the lease may have
    /// ended meanwhile; nothing would ever release a pin taken for it then.
    /// </summary>
    private bool WGLeaseLive(Entity<SalvageMagnetDataComponent> data, int seed)
    {
        return !TerminatingOrDeleted(data) && data.Comp.EndTime != null && data.Comp.ActiveSeed == seed;
    }

    /// <summary>Pins salvage an offer brought in, if its lease is still running.</summary>
    private void WGPinOffered(Entity<SalvageMagnetDataComponent> data, int seed, EntityUid salvage)
    {
        if (WGLeaseLive(data, seed))
            WGPinSalvage(data, salvage);
    }

    /// <summary>Pins the grid the magnet sits on until the lease ends, if it is still running.</summary>
    private void WGPinMagnetGrid(Entity<SalvageMagnetDataComponent> data, int seed, EntityUid? grid)
    {
        if (grid is not { } pinned || !WGLeaseLive(data, seed))
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
