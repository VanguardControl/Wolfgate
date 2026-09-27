#nullable enable
using System.Collections.Generic;
using Content.Server._WF.Caverns;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>The Gut's digestive acid and choked throats, on a bare airless grid.</summary>
[TestFixture]
[TestOf(typeof(WFCavernSampler))]
public sealed class CavernGutTest
{
    private const string Acid = "WFCavernDigestiveAcid";
    private const string Tendons = "WFCavernTendons";
    private const string Floor = "WFCavernFloorGut";

    /// <summary>Seconds the mobs stand still before they are checked.</summary>
    private const float StandSeconds = 3f;

    /// <summary>The Caustic a human must take in that time; the acid deals 5 a second.</summary>
    private const int MinCaustic = 10;

    /// <summary>
    /// With no air to burn in, a human standing in acid takes Caustic damage while a flesh creature beside it takes
    /// none, and the stomach floor under the pool is still there with nothing spilled on it.
    /// </summary>
    [Test]
    public async Task AcidBurnsWithoutAirAndSparesFloor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap(true, Floor);
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var human = EntityUid.Invalid;
        var flesh = EntityUid.Invalid;
        var fleshTile = new Vector2i(3, 0);

        await server.WaitPost(() =>
        {
            LayFloor(maps, tileDefs, map.Grid, fleshTile.X);
            entMan.SpawnEntity(Acid, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            entMan.SpawnEntity(Acid, new EntityCoordinates(map.Grid, 3.5f, 0.5f));
            human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            flesh = entMan.SpawnEntity("MobFleshJared", new EntityCoordinates(map.Grid, 3.5f, 0.5f));
        });

        await pair.RunSeconds(StandSeconds);

        await server.WaitAssertion(() =>
        {
            var burned = Caustic(entMan, human);
            var spared = Caustic(entMan, flesh);
            TestContext.Out.WriteLine($"Human {burned} Caustic, flesh creature {spared} Caustic after {StandSeconds} s in acid.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(burned, Is.GreaterThanOrEqualTo(MinCaustic), $"A human in acid for {StandSeconds} s took only {burned} Caustic.");
                Assert.That(spared, Is.Zero, $"A flesh creature in the Gut's acid took {spared} Caustic.");

                foreach (var index in new[] { Vector2i.Zero, fleshTile })
                {
                    Assert.That(maps.TryGetTileRef(map.Grid, map.Grid.Comp, index, out var tile) && !tile.Tile.IsEmpty, Is.True,
                        $"The floor under the acid at {index} is gone.");
                    Assert.That(tileDefs[tile.Tile.TypeId].ID, Is.EqualTo(Floor), $"The floor under the acid at {index} changed.");
                }

                var puddles = entMan.EntityQueryEnumerator<PuddleComponent>();
                Assert.That(puddles.MoveNext(out _), Is.False, "The acid spilled a puddle.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A human standing in the tendons walks slower than one on bare floor beside them.</summary>
    [Test]
    public async Task TendonsSlowWalkers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap(true, Floor);
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var slowed = EntityUid.Invalid;
        var free = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            LayFloor(maps, tileDefs, map.Grid, 3);
            entMan.SpawnEntity(Tendons, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            slowed = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            free = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 3.5f, 0.5f));
        });

        await pair.RunSeconds(1f);

        await server.WaitAssertion(() =>
        {
            var inTendons = entMan.GetComponent<MovementSpeedModifierComponent>(slowed).CurrentWalkSpeed;
            var onFloor = entMan.GetComponent<MovementSpeedModifierComponent>(free).CurrentWalkSpeed;
            TestContext.Out.WriteLine($"Walk speed {inTendons} in tendons, {onFloor} on bare floor.");

            Assert.That(inTendons, Is.LessThan(onFloor * 0.8f), $"Tendons leave the walk speed at {inTendons} against {onFloor} on bare floor.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The sampler counts acid as a barrier like lava, and tendons and glow flora as open ground.</summary>
    [Test]
    public async Task SamplerClassifiesGutFeatures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var sampler = server.System<WFCavernSampler>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sampler.IsHazard(Acid), Is.True, "Digestive acid is not a walkability barrier.");
                Assert.That(sampler.IsSolid(Acid), Is.False, "Digestive acid counts as rock.");
                Assert.That(sampler.IsHazard(Tendons), Is.False, "Tendons count as a barrier; they only slow you.");
                Assert.That(sampler.IsSolid(Tendons), Is.False, "Tendons count as rock.");
                Assert.That(sampler.IsLight("WFCavernNerveCluster"), Is.True, "Nerve clusters give no light.");
                Assert.That(sampler.IsLight(Tendons), Is.False, "Tendons count as a light.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Lays the stomach floor from the test tile east to <paramref name="toX"/>, unbroken so the grid doesn't split.</summary>
    private static void LayFloor(SharedMapSystem maps, ITileDefinitionManager tileDefs, Entity<MapGridComponent> grid, int toX)
    {
        for (var x = 1; x <= toX; x++)
        {
            maps.SetTile(grid, new Vector2i(x, 0), new Tile(tileDefs[Floor].TileId));
        }
    }

    private static int Caustic(IEntityManager entMan, EntityUid mob)
    {
        return entMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.GetValueOrDefault("Caustic", FixedPoint2.Zero).Int();
    }
}
