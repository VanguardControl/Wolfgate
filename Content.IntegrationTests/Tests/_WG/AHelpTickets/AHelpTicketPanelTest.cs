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
    [TestCase(false)]
    [TestCase(true)]
    public async Task MacroAppliesToTheTicketItsReplyOpens(bool adminOnly)
    {
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
