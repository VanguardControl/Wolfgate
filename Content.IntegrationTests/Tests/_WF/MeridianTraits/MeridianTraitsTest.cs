#nullable enable
using Content.Server._WF.MeridianTraits;
using Content.Shared._WF.RMCCore.Deafness;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.MeridianTraits;

/// <summary>
/// Every Meridian trait applies to a human, and the Deaf trait survives its deafness being removed.
/// </summary>
[TestFixture]
public sealed class MeridianTraitsTest
{
    private static readonly string[] Traits =
    {
        "WFDeaf", "WFPseudobulbarAffect", "WFIlluminated", "WFRadFiend", "WFHardenedSoles", "WFCanineTraits",
    };

    [Test]
    public async Task TraitsApplyAndDeafnessStays()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var map = await pair.CreateTestMap();

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", map.MapCoords);
            foreach (var id in Traits)
            {
                entMan.AddComponents(mob, protoMan.Index<TraitPrototype>(id).Components, false);
            }

            entMan.RemoveComponent<DeafComponent>(mob);
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFPermanentDeafComponent>(mob));
            Assert.That(entMan.HasComponent<DeafComponent>(mob), "Permanent deafness should come back.");
        });

        await pair.CleanReturnAsync();
    }
}
