#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WG.AHelpTickets;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.IntegrationTests.Pair;
using Content.Server._WG.AHelpTickets;
using Content.Server.Administration.Managers;
using Content.Shared._WG.AHelpTickets;
using Robust.Client.UserInterface;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using ClientBwoinkSystem = Content.Client.Administration.Systems.BwoinkSystem;

namespace Content.IntegrationTests.Tests._WG.AHelpTickets;

/// <summary>The staff ticket panel's macros, and notices staff and players get flagged as system lines.</summary>
[TestFixture]
public sealed class AHelpTicketPanelTest
{
    private const int MaxWaitTicks = 200;
    private const string ResolveMacro = "WGMacroDone";

    /// <summary>A macro used on a player with no ticket this round applies to the ticket its reply opens.</summary>
    [Test]
    public async Task MacroAppliesToTheTicketItsReplyOpens()
    {
        const bool adminOnly = false;
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var tickets = pair.Server.System<AHelpTicketSystem>();
        var player = new NetUserId(Guid.NewGuid());
        var ui = client.ResolveDependency<IUserInterfaceManager>();
        var macro = client.ResolveDependency<IPrototypeManager>().Index<WGAHelpMacroPrototype>(ResolveMacro);
        Assert.That(macro.Action, Is.EqualTo(AHelpTicketAction.Resolve));

        await client.WaitPost(() =>
        {
            var controller = ui.GetUIController<AHelpUIController>();
            controller.EnsureUIHelper();
            controller.Open(player);
            var control = ((WGTicketAHelpUIHandler) controller.UIHelper!).Control!;
            control.AdminOnly.Pressed = adminOnly;
            control.ApplyMacro(macro);
        });

        // In steps, so a failure says which hop broke.
        var clientTickets = client.System<AHelpTicketClientSystem>();
        await WaitFor(pair, () => tickets.Tickets.Any(t => t.Player == player), "the reply to open a ticket on the server");
        await WaitFor(pair, () => clientTickets.LatestFor(player) != null, "the client to hear of the new ticket");
        await WaitFor(pair, () => tickets.Tickets.Any(t => t.Player == player && t.State == AHelpTicketState.Resolved),
            $"the macro to resolve the ticket its reply opened (server: {string.Join(", ", tickets.Tickets.Where(t => t.Player == player).Select(t => $"#{t.Key} {t.State}"))}; client: {clientTickets.LatestFor(player)?.State})");
        Assert.Multiple(() =>
        {
            Assert.That(tickets.GetOpenTicket(player), Is.Null);
            Assert.That(tickets.Tickets.Count(t => t.Player == player), Is.EqualTo(1), "The reply should open exactly one ticket.");
        });

        await client.WaitPost(() => ui.GetUIController<AHelpUIController>().UIHelper?.Close());
        await pair.CleanReturnAsync();
    }

    /// <summary>A macro goes to the player's open ticket even when it is an older one reopened after a newer one closed.</summary>
    [Test]
    public async Task MacroFindsAReopenedOlderTicket()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var tickets = pair.Server.System<AHelpTicketSystem>();
        var clientTickets = client.System<AHelpTicketClientSystem>();
        var clientBwoink = client.System<ClientBwoinkSystem>();
        var player = new NetUserId(Guid.NewGuid());
        var ui = client.ResolveDependency<IUserInterfaceManager>();
        var macro = client.ResolveDependency<IPrototypeManager>().Index<WGAHelpMacroPrototype>(ResolveMacro);

        AHelpTicketInfo Ticket(int nth) => tickets.Tickets.Where(t => t.Player == player).OrderBy(t => t.Key).ElementAt(nth);

        // Ticket A, then B: each opened by a staff message and resolved.
        for (var n = 0; n < 2; n++)
        {
            var nth = n;
            await client.WaitPost(() => clientBwoink.Send(player, $"staff message {nth}", false, false));
            await WaitFor(pair, () => tickets.Tickets.Count(t => t.Player == player) == nth + 1, $"ticket {nth} to open");
            await WaitFor(pair, () => clientTickets.LatestFor(player)?.Key == Ticket(nth).Key, $"the client to hear of ticket {nth}");
            await client.WaitPost(() => clientTickets.Act(Ticket(nth).Key, AHelpTicketAction.Resolve));
            await WaitFor(pair, () => Ticket(nth).State == AHelpTicketState.Resolved, $"ticket {nth} to close");
        }

        // Reopen A; B stays the newest, closed.
        await client.WaitPost(() => clientTickets.Act(Ticket(0).Key, AHelpTicketAction.Reopen));
        await WaitFor(pair, () => clientTickets.OpenFor(player)?.Key == Ticket(0).Key, "ticket A to reopen");

        await client.WaitPost(() =>
        {
            var controller = ui.GetUIController<AHelpUIController>();
            controller.EnsureUIHelper();
            controller.Open(player);
            var control = ((WGTicketAHelpUIHandler) controller.UIHelper!).Control!;
            control.AdminOnly.Pressed = true;
            control.ApplyMacro(macro);
        });

        await WaitFor(pair, () => Ticket(0).State == AHelpTicketState.Resolved, "the macro to resolve the reopened ticket");
        Assert.That(Ticket(1).State, Is.EqualTo(AHelpTicketState.Resolved), "The newer ticket stays as it was.");

        await client.WaitPost(() => ui.GetUIController<AHelpUIController>().UIHelper?.Close());
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A macro reply is tied to its ticket on the server: with an older closed ticket about (whose updates the client
    /// may still be getting), the macro resolves only the ticket its reply opens.
    /// </summary>
    [Test]
    public async Task MacroReplyTouchesOnlyItsOwnTicket()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var tickets = pair.Server.System<AHelpTicketSystem>();
        var clientTickets = client.System<AHelpTicketClientSystem>();
        var clientBwoink = client.System<ClientBwoinkSystem>();
        var player = new NetUserId(Guid.NewGuid());

        await client.WaitPost(() => clientBwoink.Send(player, "first", false, false));
        await WaitFor(pair, () => tickets.Tickets.Any(t => t.Player == player), "the first ticket");
        var older = tickets.Tickets.Single(t => t.Player == player);
        await WaitFor(pair, () => clientTickets.LatestFor(player) != null, "the client to hear of it");
        await client.WaitPost(() => clientTickets.Act(older.Key, AHelpTicketAction.Resolve));
        await WaitFor(pair, () => older.State == AHelpTicketState.Resolved, "the first ticket to close");

        // An unrelated update for the older ticket goes first: a staff note lands on it, as the latest ticket.
        await client.WaitPost(() =>
        {
            clientBwoink.Send(player, "note on the old one", false, true);
            clientTickets.SendMacroReply(player, "macro reply", false, false, ResolveMacro, AHelpTicketAction.Resolve);
        });
        await WaitFor(pair, () => older.HasNotes, "the note on the older ticket");
        await WaitFor(pair, () => tickets.Tickets.Count(t => t.Player == player) == 2
                                  && tickets.Tickets.Single(t => t.Player == player && t.Key != older.Key).State == AHelpTicketState.Resolved,
            "the macro to resolve the ticket its reply opened");

        var newer = tickets.Tickets.Single(t => t.Player == player && t.Key != older.Key);
        Assert.Multiple(() =>
        {
            Assert.That(older.State, Is.EqualTo(AHelpTicketState.Resolved), "The older ticket is untouched.");
            Assert.That(tickets.MacrosOf(older.Key), Is.Null, "The macro is not recorded on the older ticket.");
            Assert.That(tickets.MacrosOf(newer.Key), Is.EqualTo(ResolveMacro), "The macro is recorded on the ticket its reply opened.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ticket open in a staffer's panel lists them as looking at it until the panel closes, and asking for help puts
    /// a staff line in the ticket.
    /// </summary>
    [Test]
    public async Task ViewersAndAssistance()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var tickets = pair.Server.System<AHelpTicketSystem>();
        var clientTickets = client.System<AHelpTicketClientSystem>();
        var clientBwoink = client.System<ClientBwoinkSystem>();
        var staffName = pair.Server.PlayerMan.Sessions.Single().Name;
        var player = new NetUserId(Guid.NewGuid());
        var ui = client.ResolveDependency<IUserInterfaceManager>();

        var received = new List<ClientBwoinkSystem.BwoinkTextMessage>();
        EventHandler<ClientBwoinkSystem.BwoinkTextMessage> onReceived = (_, msg) => received.Add(msg);
        clientBwoink.OnBwoinkTextMessageRecieved += onReceived;

        await client.WaitPost(() => clientBwoink.Send(player, "staff message", false, false));
        await WaitFor(pair, () => clientTickets.LatestFor(player) != null, "the client to hear of the ticket");
        var key = clientTickets.LatestFor(player)!.Key;
        AHelpTicketInfo Ticket() => tickets.Tickets.Single(t => t.Key == key);

        await client.WaitPost(() =>
        {
            var controller = ui.GetUIController<AHelpUIController>();
            controller.EnsureUIHelper();
            controller.Open(player);
        });
        await WaitFor(pair, () => Ticket().Viewers.Contains(staffName), "the staffer to show as looking at the ticket");

        // The only staffer online has nobody to ask, so the ticket doesn't claim help was asked for.
        await client.WaitPost(() => clientTickets.Assist(key));
        await pair.RunTicksSync(20);
        Assert.That(received.Any(m => m.UserId == player && m.WGSystem && m.Text.Contains(staffName) && m.Text.Contains("help")), Is.False,
            "With no other staff online, asking for help notes nothing in the ticket.");

        await client.WaitPost(() => ui.GetUIController<AHelpUIController>().UIHelper?.Close());
        await WaitFor(pair, () => Ticket().Viewers.Count == 0, "closing the panel to stop the staffer looking");

        clientBwoink.OnBwoinkTextMessageRecieved -= onReceived;
        await pair.CleanReturnAsync();
    }

    /// <summary>An admin-only note on a player with no ticket opens none, and the macro on it applies to nothing.</summary>
    [Test]
    public async Task AdminOnlyNoteOpensNoTicket()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var tickets = pair.Server.System<AHelpTicketSystem>();
        var clientTickets = client.System<AHelpTicketClientSystem>();
        var clientBwoink = client.System<ClientBwoinkSystem>();
        var player = new NetUserId(Guid.NewGuid());

        await client.WaitPost(() =>
        {
            clientBwoink.Send(player, "staff note", false, true);
            clientTickets.SendMacroReply(player, "noted", false, true, ResolveMacro, AHelpTicketAction.Resolve);
        });
        await pair.RunTicksSync(20);

        Assert.That(tickets.Tickets.Any(t => t.Player == player), Is.False, "A staff note alone mustn't open a ticket.");
        await pair.CleanReturnAsync();
    }

    /// <summary>With no staff online and no webhook, the "nobody heard you" notice is a system line, not activity.</summary>
    [Test]
    public async Task NoStaffNoticeIsASystemLine()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var admins = server.ResolveDependency<IAdminManager>();
        var session = server.PlayerMan.Sessions.Single();
        var userId = client.Session!.UserId;
        var clientBwoink = client.System<ClientBwoinkSystem>();

        var received = new List<ClientBwoinkSystem.BwoinkTextMessage>();
        EventHandler<ClientBwoinkSystem.BwoinkTextMessage> onReceived = (_, msg) => received.Add(msg);
        clientBwoink.OnBwoinkTextMessageRecieved += onReceived;

        await server.WaitPost(() => admins.DeAdmin(session));
        await pair.RunTicksSync(5);

        await client.WaitPost(() => clientBwoink.Send(userId, "anyone there?", true, false));
        var notice = server.ResolveDependency<ILocalizationManager>().GetString("bwoink-system-starmute-message-no-other-users");
        await WaitFor(pair, () => received.Any(m => m.Text == notice), "the no-staff notice");
        Assert.That(received.First(m => m.Text == notice).WGSystem, "The no-staff notice is a system line.");

        clientBwoink.OnBwoinkTextMessageRecieved -= onReceived;
        await server.WaitPost(() => admins.ReAdmin(session));
        await pair.CleanReturnAsync();
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
