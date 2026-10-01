using Content.Server._Crescent.ShipShields;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    [Dependency] private ShipShieldsSystem _wfFtlShields = default!;

    /// <summary>Drops departing fields as soon as spoolup has successfully started.</summary>
    private void SuppressWolfgateFtlShields(EntityUid shuttle)
    {
        var departing = new HashSet<EntityUid>();
        GetAllDockedShuttles(shuttle, departing);
        foreach (var grid in departing)
            _wfFtlShields.SuppressWolfgateShieldForFtl(grid);
    }
}
