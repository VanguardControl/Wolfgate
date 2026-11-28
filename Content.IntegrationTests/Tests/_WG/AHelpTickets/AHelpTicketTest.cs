#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WG.AHelpTickets;
using Content.Server.Administration;
using Content.Server.Database;
using Content.Shared._WG.AHelpTickets;
using Content.Shared.GameTicking;
using Robust.Shared.GameObjects;
using ClientBwoinkSystem = Content.Client.Administration.Systems.BwoinkSystem;
using ServerBwoinkSystem = Content.Server.Administration.Systems.BwoinkSystem;

namespace Content.IntegrationTests.Tests._WG.AHelpTickets;

/// <summary>A player's ahelp becomes a numbered ticket that staff claim and close, kept in the database.</summary>
[TestFixture]
public sealed class AHelpTicketTest
{
    private const int MaxWaitTicks = 200;

    [Test]
    public async Task TicketLifecycleIsStored()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var userId = client.Session!.UserId;
        var tickets = server.System<AHelpTicketSystem>();
        var serverBwoink = server.System<ServerBwoinkSystem>();
        var clientBwoink = client.System<ClientBwoinkSystem>();

        // The test client is host, so it gets the staff copy of every line.
        var received = new List<ClientBwoinkSystem.BwoinkTextMessage>();
        EventHandler<ClientBwoinkSystem.BwoinkTextMessage> onReceived = (_, msg) => received.Add(msg);
        clientBwoink.OnBwoinkTextMessageRecieved += onReceived;

        await client.WaitPost(() => clientBwoink.Send(userId, "my ship is stuck", true, false));
        await WaitFor(pair, () => tickets.GetOpenTicket(userId) != null, "a ticket to open");

        var first = tickets.GetOpenTicket(userId)!;
        Assert.That(first.State, Is.EqualTo(AHelpTicketState.Open));

        await server.WaitPost(() => serverBwoink.OnWebhookBwoinkTextMessage(
            new ClientBwoinkSystem.BwoinkTextMessage(userId, ClientBwoinkSystem.SystemUserId, "on it"),
            new ServerApi.BwoinkActionBody { Text = "on it", Username = "Staff", Guid = userId.UserId, WebhookUpdate = false }));
        await WaitFor(pair, () => tickets.GetOpenTicket(userId)?.State == AHelpTicketState.Claimed, "the staff reply to claim it");
        Assert.Multiple(() =>
        {
            Assert.That(tickets.GetOpenTicket(userId)!.AssigneeName, Is.EqualTo("Staff"));
            Assert.That(tickets.GetOpenTicket(userId)!.FirstReply, Is.Not.Null);
            Assert.That(serverBwoink.IsAHelpClaimed(userId));
        });

        // The upstream Resolve button closes it too.
        await client.WaitPost(() => clientBwoink.SendResolve(userId));
        await WaitFor(pair, () => tickets.GetOpenTicket(userId) == null, "the ticket to close");
        await WaitFor(pair, () => received.Any(m => m.WGSystem), "the close notice");
        Assert.Multiple(() =>
        {
            Assert.That(first.State, Is.EqualTo(AHelpTicketState.Resolved));
            Assert.That(serverBwoink.IsAHelpClaimed(userId), Is.False);
            // A Discord reply shares SystemUserId with notices, but only notices are flagged as system lines.
            Assert.That(received.First(m => m.Text.EndsWith(": on it")).WGSystem, Is.False, "A Discord staff reply counts as activity.");
            Assert.That(received.Last(m => m.WGSystem).Text, Does.Contain("closed"), "The close notice is a system line.");
        });

        // A staff note after the close belongs to that ticket; it opens nothing.
        await server.WaitPost(() => serverBwoink.OnWebhookBwoinkTextMessage(
            new ClientBwoinkSystem.BwoinkTextMessage(userId, ClientBwoinkSystem.SystemUserId, "note to self", adminOnly: true),
            new ServerApi.BwoinkActionBody { Text = "note to self", Username = "Staff", Guid = userId.UserId, WebhookUpdate = false }));
        await WaitFor(pair, () => first.HasNotes, "the note on the closed ticket");
        Assert.That(tickets.GetOpenTicket(userId), Is.Null, "An admin-only note doesn't reopen or open a ticket.");

        await client.WaitPost(() => clientBwoink.Send(userId, "one more thing", true, false));
        await WaitFor(pair, () => tickets.GetOpenTicket(userId) != null, "a second ticket");
        var second = tickets.GetOpenTicket(userId)!;
        Assert.That(second.Key, Is.Not.EqualTo(first.Key), "A message after a close opens a new ticket.");

        await Flush(pair, tickets);
        Assert.That(first.Number, Is.GreaterThan(0), "The closed ticket should have been stored.");

        var db = server.ResolveDependency<IServerDbManager>();
        var (stored, lines) = await db.GetAhelpTicketTranscriptAsync(first.Number);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.State, Is.EqualTo(AHelpTicketState.Resolved));
            Assert.That(lines.Select(l => l.Text), Does.Contain("my ship is stuck"));
            Assert.That(lines.Any(l => l.Source == "discord" && l.Text == "on it"));
        });

        var found = await db.SearchAhelpTicketsAsync("SHIP IS STUCK", 10);
        Assert.That(found.Select(f => f.Number), Does.Contain(first.Number), "Search should match what was said, ignoring case.");

        // Round end closes what is still open.
        await server.WaitPost(() => server.EntMan.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent()));
        await Flush(pair, tickets);
        Assert.Multiple(() =>
        {
            Assert.That(second.State, Is.EqualTo(AHelpTicketState.ClosedRoundEnd));
            Assert.That(tickets.GetOpenTicket(userId), Is.Null);
        });

        clientBwoink.OnBwoinkTextMessageRecieved -= onReceived;
        await pair.CleanReturnAsync();
    }

    private static async Task Flush(TestPair pair, AHelpTicketSystem tickets)
    {
        // FlushAsync waits for a periodic flush already running, then writes everything queued before the call.
        Task? flush = null;
        await pair.Server.WaitPost(() => flush = tickets.FlushAsync());
        await WaitFor(pair, () => flush!.IsCompleted, "the tickets to be written");
        Assert.That(flush!.IsCompletedSuccessfully, "The flush should not throw.");
    }

    private static async Task WaitFor(TestPair pair, Func<bool> condition, string what)
    {
        for (var i = 0; i < MaxWaitTicks && !condition(); i++)
        {
            await pair.RunTicksSync(1);
        }

        Assert.That(condition(), $"Timed out waiting for {what}.");
    }
}
