using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Server._WF.ShipAccess;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Systems;
using Content.Shared.Inventory;
using Content.Shared.StationRecords;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Issues ordinary crew ID cards and enrolls them on the ship where the crew is spawned.</summary>
public sealed partial class WFCrewAccessSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedIdCardSystem _id = default!;
    [Dependency] private WFShipAccessSystem _access = default!;
    [Dependency] private WFShipAccessServerSystem _ships = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private StationRecordKeyStorageSystem _keys = default!;

    private static readonly EntProtoId CrewCard = "PassengerIDCard";

    /// <summary>Registers a newly spawned crewman's worn card only on its spawn ship, when that ship manages access.</summary>
    public void RegisterSpawnShip(EntityUid uid)
    {
        if (Transform(uid).GridUid is not { } grid || !TryComp<WFShipAccessComponent>(grid, out var ship))
            return;

        if (!_access.TryGetWornCard(uid, out var card))
        {
            card = Spawn(CrewCard, Transform(uid).Coordinates);
            if (!_inventory.TryEquip(uid, card, "id", silent: true, force: true))
            {
                Del(card);
                return;
            }
            _id.TryChangeFullName(card, Name(uid));
        }

        if (!_access.TryGetKey(card, out _))
        {
            var records = EnsureComp<StationRecordsComponent>(grid);
            var key = _records.AddRecordEntry(grid, new GeneralStationRecord { Name = Name(uid) }, records);
            if (!key.IsValid())
                return;
            var storage = EnsureComp<StationRecordKeyStorageComponent>(card);
            _keys.AssignKey(card, key, storage);
        }

        _ships.TryAddCard((grid, ship), card, Name(uid));
    }
}
