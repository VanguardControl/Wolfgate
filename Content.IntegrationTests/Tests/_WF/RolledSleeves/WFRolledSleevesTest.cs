#nullable enable
using System.Linq;
using Content.Client.Inventory;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.RolledSleeves;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.RolledSleeves;

/// <summary>Any jumpsuit gets rollable sleeves, and rolling them puts the sleeve mask on its worn layers.</summary>
[TestFixture]
public sealed class WFRolledSleevesTest : InteractionTest
{
    private const string MaskSuffix = "-wf-rolled-sleeves";

    [TestCase("ClothingUniformJumpsuitEngineering")]
    [TestCase("ClothingUniformJumpsuitColorGrey")]
    public async Task RollingMasksTheWornLayers(string suitId)
    {
        var target = await SpawnTarget("MobHuman");
        var mob = ToServer(target);
        EntityUid suit = default;

        await Server.WaitAssertion(() =>
        {
            suit = SEntMan.SpawnEntity(suitId, SEntMan.GetCoordinates(TargetCoords));
            Assert.That(SEntMan.HasComponent<WFRolledSleevesComponent>(suit), Is.True,
                "The jumpsuit didn't get rollable sleeves.");
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(mob, suit, "jumpsuit", force: true), Is.True);
        });
        await RunTicks(5);
        await AssertMasked(target, false);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFRolledSleevesComponent>(suit);
            SEntMan.System<WFRolledSleevesSystem>().Toggle((suit, comp), mob);
        });
        await RunTicks(5);
        await AssertMasked(target, true);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFRolledSleevesComponent>(suit);
            SEntMan.System<WFRolledSleevesSystem>().Toggle((suit, comp), mob);
        });
        await RunTicks(5);
        await AssertMasked(target, false);
    }

    private async Task AssertMasked(NetEntity target, bool rolled)
    {
        await Client.WaitAssertion(() =>
        {
            var sprite = CEntMan.GetComponent<SpriteComponent>(ToClient(target));
            var slots = CEntMan.GetComponent<InventorySlotsComponent>(ToClient(target));
            var maskKeys = slots.VisualLayerKeys["jumpsuit"].Where(k => k.EndsWith(MaskSuffix)).ToList();
            if (!rolled)
            {
                Assert.That(maskKeys, Is.Empty, "Unrolled sleeves still have a mask layer.");
                return;
            }

            Assert.That(maskKeys, Is.Not.Empty, "Rolled sleeves have no mask layer.");
            foreach (var maskKey in maskKeys)
            {
                var suitKey = maskKey[..^MaskSuffix.Length];
                Assert.That(sprite.LayerMapTryGet(suitKey, out var suitIndex), Is.True);
                Assert.That(sprite.LayerMapTryGet(maskKey, out var maskIndex), Is.True);
                Assert.That(maskIndex, Is.LessThan(suitIndex), "The mask layer must come before the suit layer.");

                var shader = ((SpriteComponent.Layer) sprite[suitIndex]).ShaderPrototype?.Id;
                Assert.That(shader, Is.AnyOf("WFRolledSleeves", "WFRolledSleevesDisplaced"));
            }
        });
    }
}
