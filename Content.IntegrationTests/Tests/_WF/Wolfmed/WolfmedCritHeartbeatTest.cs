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
        await pair.RunTicksSync(30);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "The player did not attach to the new body.");

        await AssertHeartbeat(pair, body, MobState.Alive, false,
            "Precondition: an alive player hears no heartbeat.");
        await AssertHeartbeat(pair, body, MobState.Critical, true,
            "Entering crit must start the loop.");
        await AssertHeartbeat(pair, body, MobState.Alive, false,
            "Recovering from crit must stop the loop.");
        await AssertHeartbeat(pair, body, MobState.Critical, true,
            "Re-entering crit must restart the loop.");
        await AssertHeartbeat(pair, body, MobState.Dead, false,
            "Dying out of crit must stop the loop too.");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Holds the body in a state until the client's copy agrees and reports the expected heartbeat.
    /// </summary>
    /// <remarks>
    /// The state has to be held rather than set once: this mob carries no damage, so its thresholds pull
    /// it straight back to Alive, and on a recycled pair the client can lose that race and read as a
    /// failure of the heartbeat instead of one of the wait.
    /// </remarks>
    private static async Task AssertHeartbeat(TestPair pair, EntityUid body, MobState state, bool active, string because)
    {
        var mobState = pair.Server.EntMan.System<MobStateSystem>();
        var heartbeat = pair.Client.System<WolfmedCritHeartbeatSystem>();
        var clientState = MobState.Invalid;
        var heard = !active;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            await pair.Server.WaitPost(() => mobState.ChangeMobState(body, state));
            await pair.RunTicksSync(10);
            await pair.Client.WaitPost(() =>
            {
                heard = heartbeat.Active;
                clientState = pair.Client.EntMan.TryGetComponent(pair.ToClientUid(body), out MobStateComponent? comp)
                    ? comp.CurrentState
                    : MobState.Invalid;
            });

            if (clientState == state && heard == active)
                return;
        }

        Assert.Fail($"{because} (client state {clientState}, heartbeat {heard}).");
    }
}
