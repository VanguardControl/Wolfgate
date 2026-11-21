#nullable enable
using Content.Client._WG.JoinQueue;
using Content.Shared._WG.JoinQueue;
using Content.Shared.CCVar;
using Robust.Client.State;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using ClientQueue = Content.Client._WG.JoinQueue.JoinQueueManager;
using ServerQueue = Content.Server._WG.JoinQueue.JoinQueueManager;

namespace Content.IntegrationTests.Tests._WG.JoinQueue;

/// <summary>
/// A player who connects to a full server waits in the queue, connected but out of the game, and is let in when a
/// slot opens.
/// </summary>
[TestFixture]
[TestOf(typeof(ServerQueue))]
public sealed class JoinQueueTest
{
    [Test]
    public async Task FullServerQueuesThenAdmits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var serverQueue = server.ResolveDependency<ServerQueue>();
        var clientQueue = client.ResolveDependency<ClientQueue>();
        var states = client.ResolveDependency<IStateManager>();
        var players = server.ResolveDependency<ISharedPlayerManager>();

        var cap = cfg.GetCVar(CCVars.SoftMaxPlayers);
        var adminsCount = cfg.GetCVar(CCVars.AdminsCountForMaxPlayers);
        var bypass = cfg.GetCVar(CCVars.AdminBypassMaxPlayers);
        await server.WaitPost(() =>
        {
            cfg.SetCVar(CCVars.SoftMaxPlayers, 0);
            cfg.SetCVar(CCVars.AdminsCountForMaxPlayers, true);
            cfg.SetCVar(CCVars.AdminBypassMaxPlayers, false);
        });

        var screen = states.CurrentState.GetType();
        client.SetConnectTarget(server);
        await client.WaitPost(() => client.ResolveDependency<IClientNetManager>().ClientConnect(null!, 0, null!));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(players.Sessions, Has.Length.EqualTo(1));
            Assert.That(players.Sessions[0].Status, Is.EqualTo(SessionStatus.Connected), "the full server let the player into the game");
            Assert.That(serverQueue.WaitingCount, Is.EqualTo(1));

            // The hub count includes the queue unless the setting is off.
            Assert.That(serverQueue.HiddenFromStatus, Is.Zero);
            cfg.SetCVar(JoinQueueCVars.CountInStatus, false);
            Assert.That(serverQueue.HiddenFromStatus, Is.EqualTo(1));
            cfg.SetCVar(JoinQueueCVars.CountInStatus, true);
        });
        await client.WaitAssertion(() =>
        {
            Assert.That(states.CurrentState, Is.TypeOf<JoinQueueState>());
            Assert.That(clientQueue.Last, Is.Not.Null);
            Assert.That(clientQueue.Last!.Position, Is.EqualTo(1));
            Assert.That(clientQueue.Last.Total, Is.EqualTo(1));
            Assert.That(clientQueue.Last.Players, Is.Zero, "a waiting player was counted as playing");
            Assert.That(clientQueue.Last.Cap, Is.Zero);
            Assert.That(clientQueue.Last.EstimateSeconds, Is.EqualTo(MsgJoinQueueUpdate.NoEstimate));
        });

        // A slot opens.
        await server.WaitPost(() => cfg.SetCVar(CCVars.SoftMaxPlayers, 30));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(players.Sessions[0].Status, Is.EqualTo(SessionStatus.InGame), "the open slot did not let the player in");
            Assert.That(serverQueue.WaitingCount, Is.Zero);
        });
        await client.WaitAssertion(() =>
        {
            // The lobby or the game takes the screen from here; the test server has neither.
            Assert.That(clientQueue.Entering, Is.True, "the client was not told its slot opened");
            Assert.That(clientQueue.Last, Is.Null);
        });

        // Nothing on the test server takes the client off the queue screen.
        await client.WaitPost(() => states.RequestStateChange(screen));
        await server.WaitPost(() =>
        {
            cfg.SetCVar(CCVars.SoftMaxPlayers, cap);
            cfg.SetCVar(CCVars.AdminsCountForMaxPlayers, adminsCount);
            cfg.SetCVar(CCVars.AdminBypassMaxPlayers, bypass);
        });
        await pair.CleanReturnAsync();
    }
}
