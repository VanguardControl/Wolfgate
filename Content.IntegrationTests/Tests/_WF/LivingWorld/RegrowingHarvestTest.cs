#nullable enable
using System.Linq;
using Content.Server._WF.LivingWorld;
using Content.Shared._WF.LivingWorld;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.LivingWorld;

/// <summary>A regrowing plant yields its loot once, nothing while bare, and again after it regrows.</summary>
[TestFixture]
[TestOf(typeof(WFRegrowingHarvestSystem))]
public sealed class RegrowingHarvestTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: GoonMushrooms
  id: WFTestFloraMushroomPatch
  components:
  - type: WFRegrowingHarvest
    regrowTime: 2
    harvestTime: 0
    table: !type:AllSelector
      children:
      - id: WFWildMushroom
";

    [Test]
    public async Task HarvestsOnceThenRegrows()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid patch = default;
        await server.WaitPost(() => patch = entMan.SpawnEntity("WFTestFloraMushroomPatch", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f)));

        int Mushrooms() => entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == "WFWildMushroom");
        var harvest = server.System<WFRegrowingHarvestSystem>();
        await server.WaitAssertion(() =>
        {
            var ent = (patch, entMan.GetComponent<WFRegrowingHarvestComponent>(patch));
            harvest.Harvest(ent, null);
            Assert.That(Mushrooms(), Is.EqualTo(1), "Picking yields the loot.");
            Assert.That(ent.Item2.Harvested, Is.True);
            harvest.Harvest(ent, null);
            Assert.That(Mushrooms(), Is.EqualTo(1), "A bare plant yields nothing.");
        });
        // Regrowth is checked once a second, so give it a couple of checks past the two-second regrow.
        await pair.RunSeconds(5);

        await server.WaitAssertion(() =>
        {
            var ent = (patch, entMan.GetComponent<WFRegrowingHarvestComponent>(patch));
            Assert.That(ent.Item2.Harvested, Is.False, "It should regrow.");
            harvest.Harvest(ent, null);
            Assert.That(Mushrooms(), Is.EqualTo(2), "A regrown plant yields again.");
            Assert.That(entMan.Deleted(patch), Is.False, "Harvesting leaves the plant standing.");
        });
        await pair.CleanReturnAsync();
    }
}
