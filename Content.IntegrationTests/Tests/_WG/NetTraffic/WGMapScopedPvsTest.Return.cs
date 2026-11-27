using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WG.NetTraffic;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NetTraffic;

public sealed partial class WGMapScopedPvsTest
{
    /// <summary>
    /// Leaving a system and coming back brings its grids back with their walls and objects, not just their floors. The
    /// client deletes a scoped grid the tick after it leaves view; a child it hadn't yet moved out of view used to stay
    /// in nullspace on return until the player reconnected.
    /// </summary>
    [Test]
    public async Task ReturningBringsTheGridsEntitiesBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var cEm = pair.Client.EntMan;

        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));

        // One departure a tick on the client, as a grid with thousands of entities is against the default of 75.
        await pair.Client.WaitPost(() => pair.Client.CfgMan.SetCVar(CVars.NetPVSEntityExitBudget, 1));
        await pair.RunTicksSync(5);

        var home = await pair.CreateTestMap();
        var away = await pair.CreateTestMap();
        await server.WaitPost(() => server.CfgMan.SetCVar(NetTrafficCVars.MapScopedGrids, true));

        NetEntity grid = default, wall = default, item = default;
        await server.WaitPost(() =>
        {
            // The middle of the test map's one tile.
            var onTile = home.GridCoords.Offset(new Vector2(0.5f, 0.5f));
            grid = em.GetNetEntity(home.Grid.Owner);
            wall = em.GetNetEntity(em.SpawnEntity("WallSolid", onTile));
            item = em.GetNetEntity(em.SpawnEntity("Crowbar", onTile));
            server.PlayerMan.SetAttachedEntity(pair.Player!, em.SpawnEntity(null, new MapCoordinates(Vector2.Zero, home.MapId)));
        });
        await pair.RunTicksSync(30);

        try
        {
            await AssertWhereTheServerHasThem(pair, wall, item, "before leaving");

            await server.WaitPost(() =>
                server.PlayerMan.SetAttachedEntity(pair.Player!, em.SpawnEntity(null, new MapCoordinates(Vector2.Zero, away.MapId))));
            await pair.RunTicksSync(30);
            Assert.Multiple(() =>
            {
                Assert.That(cEm.TryGetEntity(grid, out _), Is.False, "the home grid stayed on the client, so its return isn't tested");
                Assert.That(cEm.TryGetEntity(wall, out _), Is.False, "the client kept the wall after its grid was deleted");
                Assert.That(cEm.TryGetEntity(item, out _), Is.False, "the client kept the crowbar after its grid was deleted");
            });

            await server.WaitPost(() =>
                server.PlayerMan.SetAttachedEntity(pair.Player!, em.SpawnEntity(null, new MapCoordinates(Vector2.Zero, home.MapId))));
            await pair.RunTicksSync(30);

            Assert.That(cEm.TryGetEntity(grid, out _), "the home grid didn't come back");
            await AssertWhereTheServerHasThem(pair, wall, item, "after coming back");
            await pair.Client.WaitAssertion(() =>
                Assert.That(cEm.GetComponent<TransformComponent>(cEm.GetEntity(wall)).Anchored,
                    "the wall isn't anchored for the returning client"));
        }
        finally
        {
            await pair.Client.WaitPost(() =>
                pair.Client.CfgMan.SetCVar(CVars.NetPVSEntityExitBudget, CVars.NetPVSEntityExitBudget.DefaultValue));
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(NetTrafficCVars.MapScopedGrids, false);
                server.CfgMan.SetCVar(CVars.NetPVS, false);
            });
            await pair.RunTicksSync(10);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Checks the client has each entity under the same parent as the server, compared by net entity.</summary>
    private static async Task AssertWhereTheServerHasThem(Pair.TestPair pair, NetEntity wall, NetEntity item, string when)
    {
        var em = pair.Server.EntMan;
        var cEm = pair.Client.EntMan;
        var expected = new Dictionary<NetEntity, NetEntity>();

        await pair.Server.WaitPost(() =>
        {
            foreach (var net in new[] { wall, item })
            {
                expected[net] = em.GetNetEntity(em.GetComponent<TransformComponent>(em.GetEntity(net)).ParentUid);
            }
        });

        await pair.Client.WaitAssertion(() =>
        {
            foreach (var (net, what) in new[] { (wall, "wall"), (item, "crowbar") })
            {
                Assert.That(cEm.TryGetEntity(net, out var uid), $"the client has no {what} {when}");
                var parent = cEm.GetComponent<TransformComponent>(uid!.Value).ParentUid;
                var parentNet = parent.IsValid() ? cEm.GetNetEntity(parent) : NetEntity.Invalid;
                Assert.That(parentNet, Is.EqualTo(expected[net]),
                    $"the client has the {what} under {cEm.ToPrettyString(parent)} {when}, the server under {expected[net]}");
            }
        });
    }
}
