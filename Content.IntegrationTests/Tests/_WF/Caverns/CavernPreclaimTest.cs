#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>A bounded world's mouth cells are all claimed before its ground is preloaded, and all inside the circle.</summary>
[TestFixture]
[TestOf(typeof(WFCavernMouthSystem))]
public sealed class CavernPreclaimTest
{
    private const int Radius = 192;

    [Test]
    public async Task EveryCellIsClaimedAheadOfThePreload()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
        });

        var world = await BuildWorld(pair, "WFSurfaceMerak");

        try
        {
            await PlanetBoundsTest.WaitPreloaded(pair, world.Ground);

            await server.WaitPost(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var bounds = entMan.GetComponent<WFPlanetBoundsComponent>(world.Ground);

                Assert.Multiple(() =>
                {
                    Assert.That(ground.Mouths.Count, Is.GreaterThan(1), "Only the gate was claimed before the preload.");
                    Assert.That(ground.Cells.Values.Any(cell => cell.State == WFCavernClaim.Unclaimed), Is.False, "A cell was left unclaimed.");

                    foreach (var mouth in ground.Mouths)
                    {
                        Assert.That(bounds.ContainsTile(mouth.Origin), Is.True, $"A mouth at {mouth.Origin} lies outside the circle.");
                    }

                    Assert.That(entMan.HasComponent<WFPlanetBoundsComponent>(world.Cavern), Is.True, "The cavern is not bounded.");
                });
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }
}
