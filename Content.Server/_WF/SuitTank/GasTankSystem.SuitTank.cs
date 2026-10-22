using Content.Shared._WF.SuitTank;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class GasTankSystem
{
    [Dependency] private WFSuitTankSlotSystem _suitTankSlot = default!;

    /// <summary>
    /// Whether the tank sits in the slot of a suit worn by its user.
    /// </summary>
    private bool InUsersSuit(EntityUid tank, EntityUid? user)
    {
        return user != null && _suitTankSlot.TryGetWearer(tank, out var wearer) && wearer == user;
    }
}
