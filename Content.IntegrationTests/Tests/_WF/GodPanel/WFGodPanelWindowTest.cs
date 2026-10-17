using System.Linq;
using Content.Client._WF.GodPanel.UI;
using Content.Shared._WF.GodPanel;
using Content.Shared.Mind;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Console;

namespace Content.IntegrationTests.Tests._WF.GodPanel;

/// <summary>
/// The God Panel's client side: the godpanelui command opens one window, the window subscribes and receives the
/// state, a free camera feed opens, and closing the window drops the subscription.
/// </summary>
[TestFixture]
[TestOf(typeof(WFGodPanelWindow))]
public sealed class WFGodPanelWindowTest
{
    [Test]
    public async Task WindowOpensReceivesStateAndUnsubscribesOnClose()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var session = pair.Player!;

        var ui = client.ResolveDependency<IUserInterfaceManager>();
        var console = client.ResolveDependency<IConsoleHost>();
        var panel = client.System<Content.Client._WF.GodPanel.WFGodPanelSystem>();

        await client.WaitPost(() => console.ExecuteCommand(WFGodPanelCommands.GodPanelUi));
        await pair.RunTicksSync(5);

        Assert.That(ui.WindowRoot.Children.OfType<WFGodPanelWindow>().Count(), Is.EqualTo(1), "The command should open the window.");

        // A second call reuses the open window.
        await client.WaitPost(() => console.ExecuteCommand(WFGodPanelCommands.GodPanelUi));
        await pair.RunTicksSync(2);
        Assert.That(ui.WindowRoot.Children.OfType<WFGodPanelWindow>().Count(), Is.EqualTo(1), "The command should not open a second window.");

        // Give the admin a body on a map, as in a round, so the window has a position to centre its scanner on.
        var map = await pair.CreateTestMap();
        await pair.RunTicksSync(5);
        var body = default(NetEntity);
        await server.WaitPost(() =>
        {
            var spawned = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var minds = server.System<SharedMindSystem>();
            minds.TransferTo(minds.GetOrCreateMind(session.UserId), spawned);
            body = server.EntMan.GetNetEntity(spawned);
        });
        await pair.RunTicksSync(5);

        var subscribed = false;
        await server.WaitPost(() => subscribed = server.System<Content.Server._WF.GodPanel.WFGodPanelSystem>().IsSubscribed(session));
        Assert.That(subscribed, "Opening the window should subscribe the admin.");

        await pair.RunTicksSync(90);

        Assert.That(panel.State, Is.Not.Null, "The window should have received a state.");
        Assert.That(panel.State!.Players.Any(player => player.Username == session.Name), Is.True,
            "The state should list the player.");

        await client.WaitPost(() =>
        {
            panel.OpenFeed(null);
            panel.OpenFeed(body);
        });
        await pair.RunTicksSync(30);
        Assert.That(panel.Feeds.Count, Is.EqualTo(2), "A free camera and a camera on the body should have opened.");
        Assert.That(panel.Feeds.Values.Count(feed => feed.Target == body), Is.EqualTo(1));

        await client.WaitPost(() => ui.WindowRoot.Children.OfType<WFGodPanelWindow>().Single().Close());
        await pair.RunTicksSync(10);

        Assert.That(ui.WindowRoot.Children.OfType<WFGodPanelWindow>(), Is.Empty, "The window should be gone.");
        Assert.That(panel.Feeds, Is.Empty, "Closing the window should close its feeds.");

        await server.WaitPost(() => subscribed = server.System<Content.Server._WF.GodPanel.WFGodPanelSystem>().IsSubscribed(session));
        Assert.That(subscribed, Is.False, "Closing the window should unsubscribe the admin.");

        await pair.CleanReturnAsync();
    }
}
