using System.Numerics;
using Content.Shared._WF.SuitTank;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SuitTank;

/// <summary>
/// A tank in a worn suit's slot feeds internals and lets go when the suit comes off.
/// </summary>
[TestOf(typeof(WFSuitTankSlotSystem))]
public sealed class WFSuitTankTest
{
    [Test]
    public async Task SuitSlotTankFeedsInternals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var inventory = entities.System<InventorySystem>();
        var itemSlots = entities.System<ItemSlotsSystem>();
        var internals = entities.System<SharedInternalsSystem>();

        EntityUid mob = default;
        EntityUid suit = default;
        EntityUid tank = default;

        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f));
            mob = entities.SpawnEntity("MobHuman", coords);
            suit = entities.SpawnEntity("ClothingOuterHardsuitEVA", coords);
            tank = entities.SpawnEntity("OxygenTankFilled", coords);
            var mask = entities.SpawnEntity("ClothingMaskBreath", coords);
            var jetpack = entities.SpawnEntity("JetpackBlueFilled", coords);

            Assert.That(entities.HasComponent<WFSuitTankSlotComponent>(suit));
            Assert.That(inventory.TryEquip(mob, mask, "mask", silent: true, force: true));
            Assert.That(inventory.TryEquip(mob, suit, "outerClothing", silent: true, force: true));
            Assert.That(itemSlots.TryInsert(suit, WFSuitTankSlotComponent.SlotId, jetpack, null), Is.False,
                "Jetpacks don't fit the suit's tank slot.");
            Assert.That(itemSlots.TryInsert(suit, WFSuitTankSlotComponent.SlotId, tank, null));
            Assert.That(inventory.TryGetSlotEntity(mob, "suitstorage", out _), Is.False,
                "The tank leaves suit storage free.");

            Assert.That(internals.FindBestGasTank(mob)?.Owner, Is.EqualTo(tank));
            Assert.That(internals.ToggleInternals(mob, mob, force: true));
        });

        // Past the gas tank system's delayed parent check.
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<InternalsComponent>(mob).GasTankEntity, Is.EqualTo(tank),
                "A tank in the worn suit stays connected.");
            Assert.That(internals.AreInternalsWorking(mob));

            Assert.That(inventory.TryUnequip(mob, "outerClothing", silent: true, force: true));
            Assert.That(entities.GetComponent<InternalsComponent>(mob).GasTankEntity, Is.Null,
                "Taking the suit off disconnects its tank.");
            Assert.That(internals.FindBestGasTank(mob), Is.Null);
        });

        await pair.CleanReturnAsync();
    }
}
