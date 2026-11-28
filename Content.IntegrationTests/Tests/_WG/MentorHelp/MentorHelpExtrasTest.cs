#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WG.MentorHelp;
using Content.Server._WG.Mentors;
using Content.Shared._WG.MentorHelp;
using Content.Shared._WG.Mentors;
using Robust.Server.Player;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._WG.MentorHelp;

/// <summary>Only Senior Mentors and staff edit canned answers; typing shows to the other side and clears on posting.</summary>
[TestFixture]
[TestOf(typeof(WGMentorHelpCannedSystem))]
public sealed class MentorHelpExtrasTest
{
    [Test]
    public async Task CannedAnswersAndTyping()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var server = pair.Server;
        var help = server.System<WGMentorHelpSystem>();
        var canned = server.System<WGMentorHelpCannedSystem>();
        var mentors = server.System<WGMentorSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        var (trainee, senior) = await MentorHelpTicketTest.TwoMentors(pair);
        Assert.That(await MentorHelpTicketTest.Run(pair, () => mentors.SetTierAsync(senior.UserId, MentorTier.Senior, null)));
        Assert.That(await MentorHelpTicketTest.Run(pair, () => mentors.SetTierAsync(trainee.UserId, MentorTier.Trainee, null)));

        var title = $"Docking {Guid.NewGuid():N}"[..20];
        var answer = new MentorCannedAnswer { Title = title, Text = "Open the shuttle console.", GuideEntry = "Shuttles" };
        Assert.That(await MentorHelpTicketTest.Run(pair, () => canned.SaveAsync(trainee, answer)), Is.False,
            "A Trainee may not edit canned answers.");
        Assert.That(await MentorHelpTicketTest.Run(pair, () => canned.SaveAsync(senior, answer)));

        var saved = canned.Answers.Single(a => a.Title == title);
        Assert.That(saved.GuideEntry, Is.EqualTo("Shuttles"));
        Assert.That(await MentorHelpTicketTest.Run(pair, () => canned.DeleteAsync(senior, saved.Id)));
        Assert.That(canned.Answers.Select(a => a.Title), Does.Not.Contain(title));

        // Typing: the mentor sees the asker typing, the asker sees a mentor typing; posting clears it.
        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(MentorHelpCVars.TicketCooldown, 0);
            Assert.That(help.Open(player, "How do I dock?", null, false), Is.EqualTo(MentorHelpOpenResult.Opened));
            var ticket = help.GetOpenTicket(player.UserId)!;

            help.SetTyping(player, null, true);
            Assert.That(help.ResponderView(ticket, senior).Typing, Is.EqualTo(new[] { ticket.CharacterName }));

            help.SetTyping(senior, ticket.Id, true);
            Assert.That(help.PlayerView(ticket).ResponderTyping);
            Assert.That(help.ResponderView(ticket, trainee).Typing, Does.Contain(senior.Name));

            Assert.That(help.PostAsResponder(senior, ticket.Id, "Use the console."));
            Assert.That(help.PlayerView(ticket).ResponderTyping, Is.False, "Posting clears the mark.");

            help.End(player);
            cfg.SetCVar(MentorHelpCVars.TicketCooldown, 30);
        });

        await MentorHelpTicketTest.RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }
}
