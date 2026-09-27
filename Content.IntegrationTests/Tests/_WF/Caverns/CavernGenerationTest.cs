#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Server.Parallax;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>What a cavern viewer standing on the gate pad actually loads.</summary>
[TestFixture]
[TestOf(typeof(BiomeSystem))]
public sealed class CavernGenerationTest
{
    /// <summary>Entities per loaded tile a cavern viewer may cause (risk 2): rock is 45-70% of a cavern, so a runaway layer shows above this.</summary>
    private const float MaxEntitiesPerTile = 0.75f;

    /// <summary>How far each way from the viewer the loaded floors are checked; chunks load at least this far.</summary>
    private const int LoadReach = 24;

    /// <summary>Ticks the viewer stands still before the load is checked.</summary>
    private const int SettleTicks = 90;

    /// <summary>A viewer on each gate pad: the pad stays clear, the world's own floors load around it, and the load stays bounded.</summary>
    [Test]
    public async Task PadClearAndWorldFloors()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var biomes = server.System<BiomeSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);

        foreach (var surfaceId in Surfaces)
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var cavern = CavernOf(pair, surfaceId);
                var gate = await Gate(pair, world);
                var viewer = await PlanetFixture.AttachViewer(pair, world.Cavern, TileCentre(gate.ClimbTile));
                await pair.RunTicksSync(SettleTicks);

                await server.WaitAssertion(() =>
                {
                    var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                    var biomeComp = entMan.GetComponent<BiomeComponent>(world.Cavern);
                    var biome = (world.Cavern, biomeComp);
                    var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                    var climb = ground.ClimbPoints[gate.ClimbTile];
                    var (substrate, chamber) = Floors[surfaceId];
                    var floors = new HashSet<string>();
                    var entities = 0;
                    var anchoredCount = 0;

                    for (var x = -LoadReach; x <= LoadReach; x++)
                    for (var y = -LoadReach; y <= LoadReach; y++)
                    {
                        if (maps.TryGetTileRef(world.Cavern, grid, gate.ClimbTile + new Vector2i(x, y), out var tile) && !tile.Tile.IsEmpty)
                            floors.Add(tileDefs[tile.Tile.TypeId].ID);
                    }

                    var query = entMan.AllEntityQueryEnumerator<TransformComponent>();
                    while (query.MoveNext(out var uid, out var xform))
                    {
                        if (xform.MapUid == world.Cavern && uid != world.Cavern)
                            entities++;
                        if (xform.MapUid == world.Cavern && xform.Anchored)
                            anchoredCount++;
                    }

                    var loadedTiles = biomeComp.LoadedChunks.Count * ChunkSize * ChunkSize;
                    TestContext.Out.WriteLine($"{surfaceId}: {entities} entities ({anchoredCount} anchored) over {loadedTiles} loaded tiles; floors {string.Join(", ", floors)}.");

                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Cavern),
                            $"Precondition: {surfaceId}'s viewer left the cavern.");
                        Assert.That(biomes.WfIsChunkLoaded(biome, gate.ClimbTile + new Vector2i(LoadReach, LoadReach)), Is.True,
                            $"Precondition: {surfaceId}'s viewer never loaded the cavern around it.");

                        foreach (var index in WFCavernMouthSystem.Pad(gate.Origin, gate.Size, cavern.Mouths.PadRadius))
                        {
                            foreach (var anchored in maps.GetAnchoredEntities(world.Cavern, grid, index))
                            {
                                if (anchored != climb)
                                    Assert.Fail($"{surfaceId}: {entMan.ToPrettyString(anchored)} stands on pad tile {index}.");
                            }
                        }

                        Assert.That(floors, Does.Contain(substrate), $"{surfaceId}: no {substrate} rock floor loaded around the pad.");
                        Assert.That(floors, Does.Contain(chamber), $"{surfaceId}: no {chamber} chamber floor loaded around the pad.");
                        Assert.That(entities, Is.LessThanOrEqualTo(loadedTiles * MaxEntitiesPerTile),
                            $"{surfaceId}: {entities} entities loaded over {loadedTiles} tiles around one viewer.");
                    }
                });
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }
}
