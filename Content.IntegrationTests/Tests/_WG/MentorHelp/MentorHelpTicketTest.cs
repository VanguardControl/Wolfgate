#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WG.MentorHelp;
using Content.Server._WG.Mentors;
using Content.Server.Administration.Systems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._WG.EventStaff;
using Content.Shared._WG.MentorHelp;
using Content.Shared._WG.Mentors;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.MentorHelp;

/// <summary>A ticket runs from open to close, the asker's limits hold, timers close it, and it reaches the database.</summary>
[TestFixture]
[TestOf(typeof(WGMentorHelpSystem))]
public sealed class MentorHelpTicketTest
{
    [Test]
    public async Task Lifecycle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var server = pair.Server;
        var help = server.System<WGMentorHelpSystem>();
        var db = server.ResolveDependency<IServerDbManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        var (first, second) = await TwoMentors(pair);

        MentorTicket ticket = null!;
        await server.WaitAssertion(() =>
        {
            Assert.That(help.Open(player, new string('a', WGMentorHelpSystem.MaxLength + 1), null, false),
                Is.EqualTo(MentorHelpOpenResult.TooLong));
            Assert.That(help.Open(player, "  [bold] [/bold] ", null, false), Is.EqualTo(MentorHelpOpenResult.Empty));
            Assert.That(help.Open(player, "How do I [bold]dock[/bold]?", "ships", false), Is.EqualTo(MentorHelpOpenResult.Opened));
            Assert.That(help.Open(player, "Again?", null, false), Is.EqualTo(MentorHelpOpenResult.AlreadyOpen));

            ticket = help.GetOpenTicket(player.UserId)!;
            Assert.Multiple(() =>
            {
                Assert.That(ticket.Status, Is.EqualTo(MentorTicketStatus.Waiting));
                Assert.That(ticket.Topic, Is.EqualTo("ships"));
                Assert.That(ticket.Lines.Single().Text, Is.EqualTo("How do I dock?"), "Markup is stripped.");
                Assert.That(ticket.CharacterName, Is.Not.Empty);
            });

            // Claim, take over with a note, hand back.
            Assert.That(help.Claim(first, ticket.Id));
            Assert.That(help.Claim(second, ticket.Id), Is.False, "Already claimed.");
            Assert.That(help.Unclaim(second, ticket.Id), Is.False, "Only the claimer or staff.");
            Assert.That(help.TakeOver(second, ticket.Id, "I know docking well"));
            Assert.That(ticket.Claimer, Is.EqualTo(second.UserId));
            Assert.That(help.Transfer(second, ticket.Id, first.Name));
            Assert.That(ticket.Claimer, Is.EqualTo(first.UserId));

            Assert.That(help.PostAsResponder(second, ticket.Id, "Open the shuttle console."), "Others may still post.");
            Assert.That(help.PostAsResponder(first, ticket.Id, "Then pick a port."));
            Assert.That(help.PostAsPlayer(player, "Thanks!"));
            Assert.That(ticket.Answered);
            Assert.That(ticket.FirstAnswerAt, Is.Not.Null);

            // The asker never sees the note; the mentors do.
            Assert.That(help.PlayerView(ticket).Lines.Select(l => l.Text), Has.None.EqualTo("I know docking well"));
            Assert.That(help.ResponderView(ticket, first).Lines.Select(l => l.Text), Has.One.EqualTo("I know docking well"));

            Assert.That(help.Resolve(second, ticket.Id), Is.False, "Only the claimer or staff.");
            Assert.That(help.Resolve(first, ticket.Id));
            Assert.Multiple(() =>
            {
                Assert.That(ticket.Status, Is.EqualTo(MentorTicketStatus.Closed));
                Assert.That(ticket.CloseReason, Is.EqualTo(MentorCloseReason.Resolved));
                Assert.That(help.PostAsResponder(first, ticket.Id, "Late"), Is.False);
            });

            Assert.That(help.Rate(player, true, "[italic]Great[/italic]"));
            Assert.That(help.Rate(player, false, null), Is.False, "Once only.");
            Assert.That(ticket.RatingComment, Is.EqualTo("Great"));

            // Reopen within the window, then the asker ends it.
            Assert.That(help.Reopen(player));
            Assert.That(ticket.Status, Is.EqualTo(MentorTicketStatus.Claimed));
            Assert.That(help.End(player));
            Assert.That(ticket.CloseReason, Is.EqualTo(MentorCloseReason.Player));

            // A new ticket waits for the cooldown.
            Assert.That(help.Open(player, "Another one", null, false), Is.EqualTo(MentorHelpOpenResult.Cooldown));
            cfg.SetCVar(MentorHelpCVars.TicketCooldown, 0);
            Assert.That(help.Open(player, "Another one", null, false), Is.EqualTo(MentorHelpOpenResult.Opened));
            Assert.That(help.Tickets.ContainsKey(ticket.Id), Is.False, "The old closed ticket is dropped.");
            help.End(player);
        });

        // Everything reached the database.
        await WaitFor(pair, () => ticket.Writes.IsCompleted, "The ticket's writes never finished.");
        Assert.That(ticket.DbId, Is.Not.Null);
        var row = await Run(pair, () => db.GetMentorTicketAsync(ticket.DbId!.Value));
        Assert.Multiple(() =>
        {
            Assert.That(row!.PlayerUserId, Is.EqualTo(player.UserId.UserId));
            Assert.That(row.Topic, Is.EqualTo("ships"));
            Assert.That(row.ClaimedBy, Is.EqualTo(first.UserId.UserId));
            Assert.That(row.CloseReason, Is.EqualTo("player"));
            Assert.That(row.Rating, Is.EqualTo(1));
            Assert.That(row.RatingComment, Is.EqualTo("Great"));
            Assert.That(row.FirstAnswerAt, Is.Not.Null);
            Assert.That(row.Messages.Select(m => m.SenderRole),
                Is.EqualTo(new[] { "player", "mentor", "mentor", "mentor", "player" }));
            Assert.That(row.Messages.Select(m => m.Text), Does.Contain("I know docking well"));
        });

        await server.WaitPost(() => cfg.SetCVar(MentorHelpCVars.TicketCooldown, 30));
        await RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FallbackIdleAndRoundEnd()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var server = pair.Server;
        var help = server.System<WGMentorHelpSystem>();
        var bwoink = server.System<BwoinkSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        var (mentor, _) = await TwoMentors(pair);

        // Unclaimed past the fallback: closed and sent to staff as a gameplay ahelp.
        MentorTicket ticket = null!;
        await server.WaitPost(() =>
        {
            cfg.SetCVar(MentorHelpCVars.TicketCooldown, 0);
            cfg.SetCVar(MentorHelpCVars.FallbackMinutes, 0.01f);
            Assert.That(help.Open(player, "Nobody home?", null, false), Is.EqualTo(MentorHelpOpenResult.Opened));
            ticket = help.GetOpenTicket(player.UserId)!;
        });

        await WaitFor(pair, () => ticket.Closed, "The ticket never fell back.");
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(ticket.CloseReason, Is.EqualTo(MentorCloseReason.Fallback));
                Assert.That(bwoink.GetAHelpCategory(player.UserId), Is.EqualTo(AHelpCategory.Gameplay));
            });
        });

        // Claimed but quiet past the idle limit: closed as idle.
        await server.WaitPost(() =>
        {
            cfg.SetCVar(MentorHelpCVars.FallbackMinutes, 5f);
            cfg.SetCVar(MentorHelpCVars.IdleCloseMinutes, 0.01f);
            Assert.That(help.Open(player, "Still there?", null, false), Is.EqualTo(MentorHelpOpenResult.Opened));
            ticket = help.GetOpenTicket(player.UserId)!;
            Assert.That(help.Claim(mentor, ticket.Id));
        });

        await WaitFor(pair, () => ticket.Closed, "The ticket never closed for idling.");
        Assert.That(ticket.CloseReason, Is.EqualTo(MentorCloseReason.Idle));

        // The round ends: open tickets close and the round's tickets are forgotten.
        await server.WaitPost(() =>
        {
            cfg.SetCVar(MentorHelpCVars.IdleCloseMinutes, 20f);
            Assert.That(help.Open(player, "Last one", null, false), Is.EqualTo(MentorHelpOpenResult.Opened));
            ticket = help.GetOpenTicket(player.UserId)!;
            server.System<GameTicker>().RestartRound();
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(ticket.CloseReason, Is.EqualTo(MentorCloseReason.RoundEnd));
                Assert.That(help.Tickets, Is.Empty);
            });

            cfg.SetCVar(MentorHelpCVars.TicketCooldown, 30);
        });

        await RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>Two dummy players made mentors and on duty.</summary>
    internal static async Task<(ICommonSession, ICommonSession)> TwoMentors(TestPair pair)
    {
        var dummies = new[] { await FreshDummy(pair, "mentor"), await FreshDummy(pair, "mentor") };
        var mentors = pair.Server.System<WGMentorSystem>();
        foreach (var dummy in dummies)
        {
            await Run(pair, async () =>
            {
                await mentors.GrantAsync(dummy.UserId, MentorTier.Mentor, null, MentorRows.SourceStaff);
                return true;
            });
        }

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(dummies.All(d => mentors.IsOnDuty(d.UserId)), "The dummy mentors should be on duty.");
        });

        return (dummies[0], dummies[1]);
    }

    /// <summary>
    /// A dummy player under a name never used before: dummies keep their user id by name for the server's life, and
    /// with it any admin rank another test gave them.
    /// </summary>
    internal static async Task<ICommonSession> FreshDummy(TestPair pair, string prefix)
    {
        // Local connections are made host admins unless this is off, which would make every dummy staff.
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(CCVars.ConsoleLoginLocal, false));
        return await pair.Server.AddDummySession($"{prefix}_{Guid.NewGuid():N}"[..24]);
    }

    /// <summary>Puts back what <see cref="FreshDummy"/> turned off.</summary>
    internal static async Task RestoreLoginLocal(TestPair pair)
    {
        await pair.Server.WaitPost(() =>
            pair.Server.CfgMan.SetCVar(CCVars.ConsoleLoginLocal, CCVars.ConsoleLoginLocal.DefaultValue));
    }

    internal static async Task WaitFor(TestPair pair, Func<bool> done, string message)
    {
        var met = false;
        for (var i = 0; i < 600 && !met; i++)
        {
            await pair.RunTicksSync(1);
            await Task.Delay(5);
            await pair.Server.WaitPost(() => met = done());
        }

        Assert.That(met, message);
    }

    internal static async Task<T> Run<T>(TestPair pair, Func<Task<T>> start)
    {
        Task<T> task = null!;
        await pair.Server.WaitPost(() => task = start());
        for (var i = 0; i < 600 && !task.IsCompleted; i++)
        {
            await pair.RunTicksSync(1);
            await Task.Delay(5);
        }

        Assert.That(task.IsCompleted, Is.True, "The server call never finished.");
        return await task;
    }
}
