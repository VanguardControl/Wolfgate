using Content.Server._DV.Feroxi;
using Content.Shared.Nutrition.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Species;

/// <summary>Feroxi never get thirsty, so they never dry out into their weaker dehydrated metabolism.</summary>
[TestFixture]
public sealed class FeroxiThirstTest
{
    [Test]
    public async Task FeroxiNeverGetThirsty()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid feroxi = default;
        float start = 0;

        await server.WaitAssertion(() =>
        {
            feroxi = em.SpawnEntity("MobFeroxi", map.GridCoords);
            start = em.GetComponent<ThirstComponent>(feroxi).CurrentThirst;
        });

        await server.WaitRunTicks(server.Timing.TickRate * 60);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ThirstComponent>(feroxi).CurrentThirst, Is.EqualTo(start), "a Feroxi got thirstier.");
            Assert.That(em.GetComponent<FeroxiDehydrateComponent>(feroxi).Dehydrated, Is.False, "a Feroxi dried out.");
        });

        await pair.CleanReturnAsync();
    }
}
