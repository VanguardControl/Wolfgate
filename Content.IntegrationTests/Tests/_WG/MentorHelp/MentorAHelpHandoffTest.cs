#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WG.MentorHelp;
using Content.Server.Administration.Managers;
using Content.Shared._WG.EventStaff;
using Content.Shared._WG.MentorHelp;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using ClientBwoinkSystem = Content.Client.Administration.Systems.BwoinkSystem;
using ServerBwoinkSystem = Content.Server.Administration.Systems.BwoinkSystem;

namespace Content.IntegrationTests.Tests._WG.MentorHelp;

/// <summary>Staff move an ahelp to Mentor Help, mentors send a ticket to staff, and the asker can report it.</summary>
[TestFixture]
[TestOf(typeof(WGMentorHelpSystem))]
public sealed class MentorAHelpHandoffTest
{
    [Test]
    public async Task SendEscalateReport()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var server = pair.Server;
        var help = server.System<WGMentorHelpSystem>();
        var bwoink = server.System<ServerBwoinkSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        var (mentor, _) = await MentorHelpTicketTest.TwoMentors(pair);
        var staff = await MentorHelpTicketTest.FreshDummy(pair, "staff");
        await server.WaitPost(() => admins.PromoteHost(staff));
        await MentorHelpTicketTest.WaitFor(pair, () => admins.HasAdminFlag(staff, AdminFlags.Adminhelp), "Staff never got Adminhelp.");

        // The player asks a how-to question in the ahelp.
        await pair.Client.WaitPost(() =>
            pair.Client.System<ClientBwoinkSystem>().Send(player.UserId, "How do I fly a shuttle?", true, false));
        await MentorHelpTicketTest.WaitFor(pair, () => bwoink.LastPlayerAHelpText(player.UserId) == "How do I fly a shuttle?",
            "The ahelp line never reached the server.");

        // Staff send it to Mentor Help: a ticket opens with that text.
        await server.WaitAssertion(() =>
        {
            Assert.That(help.OpenFromAHelp(mentor, player.UserId), Is.False, "A mentor isn't staff.");
            Assert.That(help.OpenFromAHelp(staff, player.UserId));

            var ticket = help.GetOpenTicket(player.UserId)!;
            Assert.Multiple(() =>
            {
                Assert.That(ticket.FromAhelp);
                Assert.That(ticket.Lines.Single().Text, Is.EqualTo("How do I fly a shuttle?"));
                Assert.That(bwoink.IsAHelpClaimed(player.UserId), Is.False);
            });

            // A mentor sends it on to staff: closed, and the ahelp is now a gameplay one.
            Assert.That(help.Claim(mentor, ticket.Id));
            Assert.That(help.PostAsResponder(mentor, ticket.Id, "This one needs staff."));
            Assert.That(help.Escalate(mentor, ticket.Id));
            Assert.Multiple(() =>
            {
                Assert.That(ticket.CloseReason, Is.EqualTo(MentorCloseReason.Escalated));
                Assert.That(bwoink.GetAHelpCategory(player.UserId), Is.EqualTo(AHelpCategory.Gameplay));
                Assert.That(help.Escalate(mentor, ticket.Id), Is.False, "Already closed.");
            });

            // A new, answered ticket: the asker reports it once, as a player issue.
            cfg.SetCVar(MentorHelpCVars.TicketCooldown, 0);
            Assert.That(help.Open(player, "Another question", null, false), Is.EqualTo(MentorHelpOpenResult.Opened));
            var second = help.GetOpenTicket(player.UserId)!;
            Assert.That(help.Report(player), Is.False, "Nothing to report before a mentor answered.");
            Assert.That(help.PostAsResponder(mentor, second.Id, "An answer."));
            Assert.That(help.Report(player));
            Assert.That(help.Report(player), Is.False, "Once a ticket.");
            Assert.Multiple(() =>
            {
                Assert.That(second.Reported);
                Assert.That(help.PlayerView(second).Reported);
                Assert.That(bwoink.GetAHelpCategory(player.UserId), Is.EqualTo(AHelpCategory.Player));
            });

            help.End(player);
            cfg.SetCVar(MentorHelpCVars.TicketCooldown, 30);
        });

        await MentorHelpTicketTest.RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>A question Mentor Help forwarded to staff can't be sent straight back; a new ahelp line can.</summary>
    [Test]
    public async Task FallbackQuestionIsNotSentBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var server = pair.Server;
        var help = server.System<WGMentorHelpSystem>();
        var bwoink = server.System<ServerBwoinkSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        var staff = await MentorHelpTicketTest.FreshDummy(pair, "staff");
        await server.WaitPost(() => admins.PromoteHost(staff));
        await MentorHelpTicketTest.WaitFor(pair, () => admins.HasAdminFlag(staff, AdminFlags.Adminhelp), "Staff never got Adminhelp.");

        MentorTicket ticket = null!;
        await server.WaitPost(() =>
        {
            cfg.SetCVar(MentorHelpCVars.TicketCooldown, 0);
            cfg.SetCVar(MentorHelpCVars.FallbackMinutes, 0.01f);
            Assert.That(help.Open(player, "Nobody answers this", null, false), Is.EqualTo(MentorHelpOpenResult.Opened));
            ticket = help.GetOpenTicket(player.UserId)!;
        });

        await MentorHelpTicketTest.WaitFor(pair, () => ticket.Closed, "The ticket never fell back.");
        await server.WaitAssertion(() =>
        {
            Assert.That(ticket.CloseReason, Is.EqualTo(MentorCloseReason.Fallback));
            Assert.That(bwoink.LastPlayerAHelpText(player.UserId), Is.Null, "The forwarded question isn't the player's own line.");
            Assert.That(help.OpenFromAHelp(staff, player.UserId), Is.False, "It can't be sent back to Mentor Help.");
            cfg.SetCVar(MentorHelpCVars.FallbackMinutes, 5f);
        });

        // A line the player writes themselves can still be handed over.
        await pair.Client.WaitPost(() =>
            pair.Client.System<ClientBwoinkSystem>().Send(player.UserId, "Actually, how do I dock?", true, false));
        await MentorHelpTicketTest.WaitFor(pair, () => bwoink.LastPlayerAHelpText(player.UserId) == "Actually, how do I dock?",
            "The new ahelp line never reached the server.");

        await server.WaitAssertion(() =>
        {
            Assert.That(help.OpenFromAHelp(staff, player.UserId));
            help.End(player);
            cfg.SetCVar(MentorHelpCVars.TicketCooldown, 30);
        });

        await MentorHelpTicketTest.RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }
}
