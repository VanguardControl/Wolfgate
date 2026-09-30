using Content.Server._WF.ShipShields;
using Content.Shared._WF.ShipShields;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WFShipShieldShuntSystem _wfShieldShunts = default!;

    /// <summary>Includes the target ship's shield allocation in each helm refresh.</summary>
    private WFShipShieldShuntState GetWolfgateShieldShuntState(EntityUid? grid)
    {
        return _wfShieldShunts.GetState(grid);
    }
}
