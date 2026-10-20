#nullable enable
using Content.Shared._WF.Ghost;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Ghost;

/// <summary>A chat orbit link to something in nullspace (a xeno hive speaking an announcement) leaves the ghost where it is.</summary>
[TestFixture]
public sealed class GhostOrbitNullspaceTest
{
    [Test]
    public async Task WarpToNullspaceIsRefused()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid ghost = default, hive = default;
        await server.WaitPost(() =>
        {
            ghost = entities.SpawnEntity("MobObserver", map.GridCoords);
            hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            server.PlayerMan.SetAttachedEntity(pair.Player!, ghost);
        });
        await pair.RunTicksSync(5);

        await client.WaitPost(() => client.EntMan.EntityNetManager!.SendSystemNetworkMessage(new GhostOrbitWarpEvent(server.EntMan.GetNetEntity(hive))));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(ghost), "The ghost survives.");
            Assert.That(entities.GetComponent<TransformComponent>(ghost).MapUid, Is.EqualTo(map.MapUid), "The ghost stays on its map.");
        });

        await pair.CleanReturnAsync();
    }
}
