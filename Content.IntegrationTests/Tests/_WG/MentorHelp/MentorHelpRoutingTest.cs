#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WG.MentorHelp;
using Content.Server._WG.Mentors;
using Content.Server.Administration.Managers;
using Content.Server.Preferences.Managers;
using Content.Shared._WG.MentorHelp;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.MentorHelp;

/// <summary>Tickets reach on-duty mentors and staff only, spoiler-safe ones stay away from mentors in the round, and names follow D6.</summary>
[TestFixture]
[TestOf(typeof(WGMentorHelpSystem))]
public sealed class MentorHelpRoutingTest
{
    [Test]
    public async Task WhoSeesWhatAndUnderWhichName()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var server = pair.Server;
        var help = server.System<WGMentorHelpSystem>();
        var mentors = server.System<WGMentorSystem>();
        var players = server.ResolveDependency<IPlayerManager>();
        var admins = server.ResolveDependency<IAdminManager>();
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var entities = server.ResolveDependency<IEntityManager>();

        var asker = players.Sessions.Single();
        var (mentor, inRound) = await MentorHelpTicketTest.TwoMentors(pair);
        var bystander = (await server.AddDummySessions(1))[0];

        MentorTicket open = null!;
        await server.WaitAssertion(() =>
        {
            Assert.That(help.Open(asker, "How do I fly?", "ships", false), Is.EqualTo(MentorHelpOpenResult.Opened));
            open = help.GetOpenTicket(asker.UserId)!;

            // A lobby asker shows their selected character.
            var selected = prefs.GetPreferences(asker.UserId).SelectedCharacter.Name;
            Assert.That(open.CharacterName, Is.EqualTo(selected));

            Assert.Multiple(() =>
            {
                Assert.That(help.CanSee(mentor, open), "An on-duty mentor sees it.");
                Assert.That(help.CanSee(bystander, open), Is.False, "A player doesn't.");
                Assert.That(help.CanSee(asker, open), Is.False, "Nobody answers their own question.");
            });

            // Off duty: no tickets.
            Assert.That(mentors.SetDuty(mentor, false));
            Assert.That(help.CanSee(mentor, open), Is.False, "An off-duty mentor doesn't.");
            Assert.That(mentors.SetDuty(mentor, true));

            // The mentor's copy never has the asker's username; the asker's never has a mentor's.
            Assert.That(help.PostAsResponder(mentor, open.Id, "Use the console."));
            var mentorView = help.ResponderView(open, mentor);
            Assert.Multiple(() =>
            {
                Assert.That(mentorView.Asker, Is.EqualTo(selected));
                Assert.That(mentorView.AskerUsername, Is.Null);
                Assert.That(mentorView.AskerInLobby);
                Assert.That(mentorView.Lines.Select(l => l.Sender), Has.None.EqualTo(asker.Name));
                Assert.That(mentorView.Lines.Last().Sender, Is.EqualTo(mentor.Name), "Mentors see each other's names.");
                Assert.That(mentorView.ClaimedBy, Is.EqualTo(mentor.Name));
            });

            var askerView = help.PlayerView(open);
            Assert.Multiple(() =>
            {
                Assert.That(askerView.Lines.Select(l => l.Sender), Has.None.EqualTo(mentor.Name));
                Assert.That(askerView.Lines.Last().Sender,
                    Is.EqualTo(server.ResolveDependency<ILocalizationManager>().GetString("wg-mhelp-sender-mentor")));
                Assert.That(askerView.Lines.First().Own);
            });
        });

        // A mentor with a character in the round doesn't see a spoiler-safe ticket; one out of the round does.
        await server.WaitPost(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var body = entities.SpawnEntity("CrateGenericSteel", new MapCoordinates(0, 0, mapId));
            players.SetAttachedEntity(inRound, body);
            help.End(asker);
            server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>().SetCVar(MentorHelpCVars.TicketCooldown, 0);
            Assert.That(help.Open(asker, "About my secret role", null, true), Is.EqualTo(MentorHelpOpenResult.Opened));
        });

        await server.WaitAssertion(() =>
        {
            var secret = help.GetOpenTicket(asker.UserId)!;
            Assert.Multiple(() =>
            {
                Assert.That(help.InRound(inRound));
                Assert.That(help.CanSee(inRound, secret), Is.False, "A mentor in the round doesn't see it.");
                Assert.That(help.CanSee(mentor, secret), "A mentor out of the round does.");
            });
        });

        // Staff see everything, secret or not, with the username beside the character.
        await server.WaitPost(() => admins.PromoteHost(bystander));
        await MentorHelpTicketTest.WaitFor(pair, () => admins.HasAdminFlag(bystander, AdminFlags.Adminhelp),
            "The bystander never became staff.");

        await server.WaitAssertion(() =>
        {
            var secret = help.GetOpenTicket(asker.UserId)!;
            Assert.Multiple(() =>
            {
                Assert.That(help.IsStaff(bystander));
                Assert.That(help.CanSee(bystander, secret));
                Assert.That(help.ResponderView(secret, bystander).AskerUsername, Is.EqualTo(asker.Name));
            });

            help.End(asker);
            server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>().SetCVar(MentorHelpCVars.TicketCooldown, 30);
        });

        await pair.CleanReturnAsync();
    }
}
