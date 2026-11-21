#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests._WG.MentorHelp;
using Content.Server._WG.MentorHelp;
using Content.Server._WG.Mentors;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared._WG.Mentors;
using Content.Shared.Database;
using Robust.Server.Player;

namespace Content.IntegrationTests.Tests._WG.Mentors;

/// <summary>A mentor ban suspends a mentor and blocks applying; a Mentor Help mute stops tickets.</summary>
[TestFixture]
[TestOf(typeof(WGMentorSystem))]
public sealed class MentorBanTest
{
    [Test]
    public async Task MentorBanSuspendsAndBlocks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var server = pair.Server;
        var bans = server.ResolveDependency<IBanManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var mentors = server.System<WGMentorSystem>();
        var applications = server.System<WGMentorApplicationSystem>();

        var mentor = await MentorHelpTicketTest.FreshDummy(pair, "banned");
        var applicant = await MentorHelpTicketTest.FreshDummy(pair, "applicant");
        await MentorHelpTicketTest.Run(pair, async () =>
        {
            await mentors.GrantAsync(mentor.UserId, MentorTier.Mentor, null, MentorRows.SourceStaff);
            return true;
        });
        Assert.That(mentors.IsMentor(mentor.UserId));

        // The ban manager takes the mentor role string as it is and suspends the mentor at once.
        await server.WaitPost(() =>
        {
            bans.CreateRoleBan(mentor.UserId, mentor.Name, null, null, null, MentorBans.MentorRole, 60,
                NoteSeverity.None, "Testing", DateTimeOffset.UtcNow);
            bans.CreateRoleBan(applicant.UserId, applicant.Name, null, null, null, MentorBans.MentorRole, 0,
                NoteSeverity.None, "Testing", DateTimeOffset.UtcNow);
        });
        await MentorHelpTicketTest.WaitFor(pair, () => !mentors.IsMentor(mentor.UserId), "The mentor ban didn't suspend.");

        var row = await MentorHelpTicketTest.Run(pair, () => db.GetMentorAsync(mentor.UserId.UserId));
        Assert.That(row!.State, Is.EqualTo(MentorRows.StateSuspended));

        var stored = await MentorHelpTicketTest.Run(pair, () =>
            db.GetServerRoleBansAsync(null, applicant.UserId, null, null, false));
        Assert.That(stored.Select(b => b.Role), Does.Contain(MentorBans.MentorRole), "Stored without the job prefix.");

        var page = await MentorHelpTicketTest.Run(pair, () => applications.BuildPageAsync(applicant.UserId, null));
        Assert.Multiple(() =>
        {
            Assert.That(page.Blocks, Does.Contain(MentorBlock.MentorBan));
            Assert.That(page.MentorBanUntil, Is.Null, "A permanent ban has no end.");
        });

        await MentorHelpTicketTest.RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MuteStopsTickets()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var server = pair.Server;
        var bans = server.ResolveDependency<IBanManager>();
        var help = server.System<WGMentorHelpSystem>();

        // The connected client: its role bans are cached, as a real player's are.
        var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        Assert.That(help.IsMuted(player.UserId), Is.False);

        await server.WaitPost(() => bans.CreateRoleBan(player.UserId, player.Name, null, null, null, MentorBans.HelpRole, 0,
            NoteSeverity.None, "Testing", DateTimeOffset.UtcNow));
        await MentorHelpTicketTest.WaitFor(pair, () => help.IsMuted(player.UserId), "The mute never applied.");

        await server.WaitAssertion(() =>
            Assert.That(help.Open(player, "How do I fly?", null, false), Is.EqualTo(MentorHelpOpenResult.Muted)));

        await pair.CleanReturnAsync();
    }
}
