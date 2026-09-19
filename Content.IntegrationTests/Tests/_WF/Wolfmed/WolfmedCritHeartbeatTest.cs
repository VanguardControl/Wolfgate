using System.Threading.Tasks;
using Content.Client._WF.Wolfmed.Audio;
using Content.IntegrationTests.Pair;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The crit heartbeat only tracks the local player's own body: it starts on entering MobState.Critical and
/// stops on recovering, dying, or re-entering crit again after recovery.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedCritHeartbeatSystem))]
public sealed class WolfmedCritHeartbeatTest
{
    [Test]
    public async Task HeartbeatTracksLocalPlayerCritTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var mindSys = sEntMan.System<SharedMindSystem>();
        var mobState = sEntMan.System<MobStateSystem>();
        var heartbeat = client.System<WolfmedCritHeartbeatSystem>();
        var map = await pair.CreateTestMap();

        Assert.That(client.Session, Is.Not.Null, "These tests need a connected pair.");
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);

        EntityUid body = default;
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            body = sEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var mind = mindSys.CreateMind(session.UserId).Owner;
            mindSys.TransferTo(mind, body);
        });
        await pair.RunTicksSync(15);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "The player did not attach to the new body.");

        await client.WaitAssertion(() =>
            Assert.That(heartbeat.Active, Is.False, "Precondition: an alive player hears no heartbeat."));

        await server.WaitPost(() => mobState.ChangeMobState(body, MobState.Critical));
        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
        {
            var clientBody = pair.ToClientUid(body);
            var clientState = client.EntMan.GetComponent<MobStateComponent>(clientBody).CurrentState;
            Assert.That(clientState, Is.EqualTo(MobState.Critical), "Precondition: the networked MobState must have reached the client.");
            Assert.That(heartbeat.Active, Is.True, "Entering crit must start the loop.");
        });

        await server.WaitPost(() => mobState.ChangeMobState(body, MobState.Alive));
        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
            Assert.That(heartbeat.Active, Is.False, "Recovering from crit must stop the loop."));

        await server.WaitPost(() => mobState.ChangeMobState(body, MobState.Critical));
        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
            Assert.That(heartbeat.Active, Is.True, "Re-entering crit must restart the loop."));

        await server.WaitPost(() => mobState.ChangeMobState(body, MobState.Dead));
        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
            Assert.That(heartbeat.Active, Is.False, "Dying out of crit must stop the loop too."));

        await pair.CleanReturnAsync();
    }
}
