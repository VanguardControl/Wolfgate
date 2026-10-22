#nullable enable
using System.Linq;
using Content.Client.Inventory;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.RolledSleeves;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.RolledSleeves;

/// <summary>Any jumpsuit gets the roll verbs; each style cuts the worn layers or swaps in a drawn sprite.</summary>
[TestFixture]
public sealed class WFRolledSleevesTest : InteractionTest
{
    private const string MaskSuffix = "-wf-rolled-sleeves";

    [TestCase("ClothingUniformJumpsuitEngineering", null)]
    [TestCase("ClothingUniformJumpsuitColorGrey", "down")]
    public async Task StylesRestyleTheWornLayers(string suitId, string? rolledDownMask)
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
        await AssertMasked(target, null);

        foreach (var (style, mask) in new (WFJumpsuitStyle, string?)[]
                 {
                     (WFJumpsuitStyle.SleevesRolled, "sleeves"),
                     (WFJumpsuitStyle.RolledDown, rolledDownMask),
                     (WFJumpsuitStyle.Normal, null),
                 })
        {
            await Server.WaitAssertion(() =>
            {
                var comp = SEntMan.GetComponent<WFRolledSleevesComponent>(suit);
                SEntMan.System<WFRolledSleevesSystem>().SetStyle((suit, comp), style, mob);
            });
            await RunTicks(5);
            await AssertMasked(target, mask);
        }

        if (rolledDownMask != null)
            return;

        // A suit with a drawn Meridian roll-down swaps to that sprite instead of the cut.
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFRolledSleevesComponent>(suit);
            SEntMan.System<WFRolledSleevesSystem>().SetStyle((suit, comp), WFJumpsuitStyle.RolledDown, mob);
        });
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            var sprite = CEntMan.GetComponent<SpriteComponent>(ToClient(target));
            var slots = CEntMan.GetComponent<InventorySlotsComponent>(ToClient(target));
            var drawn = slots.VisualLayerKeys["jumpsuit"]
                .Where(k => sprite.LayerMapTryGet(k, out _))
                .Select(k => ((SpriteComponent.Layer) sprite[sprite.LayerMapGet(k)]).ActualRsi?.Path.ToString())
                .Any(p => p != null && p.StartsWith("/Textures/_WF/RolledSleeves/Meridian/"));
            Assert.That(drawn, Is.True, "The rolled-down suit didn't use its drawn Meridian sprite.");
        });
    }

    private async Task AssertMasked(NetEntity target, string? maskState)
    {
        await Client.WaitAssertion(() =>
        {
            var sprite = CEntMan.GetComponent<SpriteComponent>(ToClient(target));
            var slots = CEntMan.GetComponent<InventorySlotsComponent>(ToClient(target));
            var maskKeys = slots.VisualLayerKeys["jumpsuit"].Where(k => k.EndsWith(MaskSuffix)).ToList();
            if (maskState == null)
            {
                Assert.That(maskKeys, Is.Empty, "A suit worn this way still has a mask layer.");
                return;
            }

            Assert.That(maskKeys, Is.Not.Empty, "A restyled suit has no mask layer.");
            foreach (var maskKey in maskKeys)
            {
                var suitKey = maskKey[..^MaskSuffix.Length];
                Assert.That(sprite.LayerMapTryGet(suitKey, out var suitIndex), Is.True);
                Assert.That(sprite.LayerMapTryGet(maskKey, out var maskIndex), Is.True);
                Assert.That(maskIndex, Is.LessThan(suitIndex), "The mask layer must come before the suit layer.");
                Assert.That(((SpriteComponent.Layer) sprite[maskIndex]).State.Name, Is.EqualTo(maskState));

                var shader = ((SpriteComponent.Layer) sprite[suitIndex]).ShaderPrototype?.Id;
                Assert.That(shader, Is.AnyOf("WFRolledSleeves", "WFRolledSleevesDisplaced"));
            }
        });
    }
}
