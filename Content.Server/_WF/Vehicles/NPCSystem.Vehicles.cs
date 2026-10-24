using Content.Shared._WF.Vehicles.Core;
using Robust.Shared.Map;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSystem
{
    /// <summary>
    /// Where a player counts as being for waking NPCs: inside a vehicle's cabin he is wherever the vehicle is, not on
    /// the cabin's own map, so a tank rolling up wakes what's outside.
    /// </summary>
    private EntityCoordinates WfPlayerPresence(EntityUid player)
    {
        if (TryComp<VehicleInteriorOccupantComponent>(player, out var occupant) && Exists(occupant.Vehicle))
            return Transform(occupant.Vehicle).Coordinates;
        return Transform(player).Coordinates;
    }
}
