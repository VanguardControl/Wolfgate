using Content.Server.IgnitionSource;
using Content.Shared.IgnitionSource;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>Checks the lit list follows an ignition source through spawning, toggling and deletion.</summary>
[TestFixture]
[TestOf(typeof(IgnitionSourceSystem))]
public sealed class WFIgnitionLitTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFIgnitionLitDummy
  components:
  - type: IgnitionSource
    temperature: 700
    ignited: false

- type: entity
  id: WFIgnitionLitBurning
  components:
  - type: IgnitionSource
    temperature: 700
    ignited: true
";

    [Test]
    public async Task ListFollowsSource()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var system = server.System<IgnitionSourceSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var unlit = server.EntMan.SpawnEntity("WFIgnitionLitDummy", map.GridCoords);
            var burning = server.EntMan.SpawnEntity("WFIgnitionLitBurning", map.GridCoords);
            Assert.That(system.WFLit, Does.Not.Contain(unlit), "An unlit source should not be listed.");
            Assert.That(system.WFLit, Does.Contain(burning), "A source spawned lit should be listed.");

            system.SetIgnited(unlit);
            Assert.That(system.WFLit, Does.Contain(unlit));

            system.SetIgnited(unlit, false);
            Assert.That(system.WFLit, Does.Not.Contain(unlit));

            server.EntMan.DeleteEntity(burning);
            Assert.That(system.WFLit, Does.Not.Contain(burning), "A deleted source should leave the list.");
            server.EntMan.DeleteEntity(unlit);
        });

        await pair.CleanReturnAsync();
    }
}
