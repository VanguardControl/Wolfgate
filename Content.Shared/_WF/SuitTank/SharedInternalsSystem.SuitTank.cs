using Content.Shared._WF.SuitTank;
using Content.Shared.Atmos.Components;

namespace Content.Shared.Body.Systems;

public abstract partial class SharedInternalsSystem
{
    [Dependency] private WFSuitTankSlotSystem _suitTank = default!;

    /// <summary>
    /// A connectable tank in the slot of the suit the user wears.
    /// </summary>
    private Entity<GasTankComponent>? FindSuitSlotTank(EntityUid user)
    {
        if (_suitTank.TryGetWornTank(user, out var tank) && _gasTank.CanConnectToInternals(tank))
            return tank;

        return null;
    }
}
