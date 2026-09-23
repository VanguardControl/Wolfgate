#nullable enable
using System.Threading.Tasks;
using System.Linq;
using Content.Client._WF.Wolfmed.Audio;
using Content.IntegrationTests.Pair;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
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
    /// M1a: a pain faint is Critical now, but a few seconds under from pain is not the dying heartbeat; and a
    /// machine never hears a human heart (plan §3.11).
    /// </summary>
    [Test]
    public async Task HeartbeatSilentForFaintsAndMachinesTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var mindSys = sEntMan.System<SharedMindSystem>();
        var heartbeat = client.System<WolfmedCritHeartbeatSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            human = sEntMan.SpawnEntity("MobHuman", map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, human);

            // 100 on the torso and 100 on the head: 200 summed, past the 189 faint line.
            var pain = sEntMan.System<PainSystem>();
            foreach (var (part, comp) in sEntMan.System<SharedBodySystem>().GetBodyChildren(human))
            {
                if (comp.PartType is BodyPartType.Torso or BodyPartType.Head)
                    pain.SetPain(part, FixedPoint2.New(100));
            }
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var consciousness = sEntMan.GetComponent<WolfmedConsciousnessComponent>(human);
            Assert.That(consciousness.Cause, Is.EqualTo(WolfmedCause.PainFaint), "the fixture did not faint.");
            Assert.That(sEntMan.System<MobStateSystem>().IsCritical(human), Is.True);
        });
        await client.WaitPost(() =>
        {
            Assert.That(client.EntMan.GetComponent<MobStateComponent>(pair.ToClientUid(human)).CurrentState,
                Is.EqualTo(MobState.Critical), "the client never saw the faint.");
            Assert.That(heartbeat.Active, Is.False, "a pain faint started the dying heartbeat.");
        });

        EntityUid ipc = default;
        await server.WaitPost(() =>
        {
            ipc = sEntMan.SpawnEntity("MobIPC", map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, ipc);
        });
        await pair.RunTicksSync(30);

        // Held in Critical the way the organic test holds it: a machine still never hears it.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await server.WaitPost(() => sEntMan.System<MobStateSystem>().ChangeMobState(ipc, MobState.Critical));
            await pair.RunTicksSync(10);
            await client.WaitPost(() =>
                Assert.That(heartbeat.Active, Is.False, "a machine heard the human heartbeat."));
        }

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
