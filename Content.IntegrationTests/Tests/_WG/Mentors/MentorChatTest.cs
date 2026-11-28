#nullable enable
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Mentors;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared._WG.Mentors;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.IntegrationTests.Tests._WG.Mentors;

/// <summary>Mentor chat reaches mentors, is refused to players, and is kept out of replays.</summary>
[TestFixture]
[TestOf(typeof(WGMentorSystem))]
public sealed class MentorChatTest
{
    [Test]
    public async Task MentorsTalkPlayersCannot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var console = server.ResolveDependency<IConsoleHost>();
        var mentors = server.System<WGMentorSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var chat = client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();

        Assert.That((ChatChannel.AdminRelated & ChatChannel.MentorChat) != 0, "Mentor chat should stay out of replays.");

        await server.WaitPost(() =>
        {
            if (admins.IsAdmin(session, includeDeAdmin: true))
                admins.DeAdmin(session);
        });
        await pair.RunTicksSync(5);

        // A player who isn't a mentor gets nothing back.
        await server.WaitPost(() => console.ExecuteCommand(session, "msay not a mentor yet"));
        await pair.RunTicksSync(10);
        Assert.That(Find(chat, "not a mentor yet"), Is.Null);

        // A mentor, on duty or not, hears mentor chat and can select the channel.
        await MentorHelp.MentorHelpTicketTest.Run(pair, async () =>
        {
            await mentors.GrantAsync(session.UserId, MentorTier.Trainee, null, MentorRows.SourceStaff);
            return true;
        });
        await server.WaitPost(() => mentors.SetDuty(session, false));
        await pair.RunTicksSync(5);

        await server.WaitPost(() => console.ExecuteCommand(session, "msay hello mentors"));
        for (var i = 0; i < 100 && Find(chat, "hello mentors") == null; i++)
        {
            await pair.RunTicksSync(1);
        }

        Assert.Multiple(() =>
        {
            Assert.That(Find(chat, "hello mentors"), Is.Not.Null, "The mentor never heard mentor chat.");
            Assert.That((chat.CanSendChannels & ChatSelectChannel.Mentor) != 0, "A mentor may select the channel.");
        });

        await MentorHelp.MentorHelpTicketTest.Run(pair, () => mentors.RevokeAsync(session.UserId, null, "Testing"));
        await pair.RunTicksSync(5);
        Assert.That((chat.CanSendChannels & ChatSelectChannel.Mentor) == 0, "A revoked mentor may not.");

        await MentorHelp.MentorHelpTicketTest.Run(pair, () => server.ResolveDependency<IServerDbManager>().DeleteMentorAsync(session.UserId.UserId));
        await pair.CleanReturnAsync();
    }

    private static ChatMessage? Find(ChatUIController chat, string text)
    {
        return chat.History
            .Select(h => h.Msg)
            .FirstOrDefault(m => m.Channel == ChatChannel.MentorChat && m.Message.Contains(text, StringComparison.Ordinal));
    }
}
