#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Database;

namespace Content.IntegrationTests.Tests._WG.MentorHelp;

/// <summary>Tickets, their messages and canned answers round-trip through the database, and transcripts expire.</summary>
[TestFixture]
[TestOf(typeof(WolfgateMentorTicket))]
public sealed class MentorHelpDbTest
{
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

    [Test]
    public async Task TicketAndMessagesRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = pair.Server.ResolveDependency<IServerDbManager>();

        var player = Guid.NewGuid();
        var mentor = Guid.NewGuid();
        var helper = Guid.NewGuid();
        var opened = DateTime.UtcNow.AddMinutes(-10);

        var id = await Run(pair, () => db.AddMentorTicketAsync(new WolfgateMentorTicket
        {
            RoundId = 1,
            PlayerUserId = player,
            CharacterName = "Ada Vance",
            Topic = "ships",
            Newbie = true,
            OpenedAt = opened,
        }));

        await AddMessage(pair, db, id, player, "player", "How do I dock?", opened);
        await AddMessage(pair, db, id, mentor, "mentor", "Open the shuttle console.", opened.AddMinutes(1));
        await AddMessage(pair, db, id, helper, "mentor", "Then pick a port.", opened.AddMinutes(2));
        await AddMessage(pair, db, id, null, "system", "Resolved.", opened.AddMinutes(3));

        // Claim, close and rate it; the messages stay as they were.
        var ticket = await Run(pair, () => db.GetMentorTicketAsync(id));
        Assert.That(ticket, Is.Not.Null);
        ticket!.ClaimedBy = mentor;
        ticket.FirstAnswerAt = opened.AddMinutes(1);
        ticket.ClosedAt = opened.AddMinutes(3);
        ticket.CloseReason = "resolved";
        ticket.Rating = 1;
        ticket.RatingComment = "Thanks!";
        Assert.That(await Run(pair, () => db.UpdateMentorTicketAsync(ticket)), Is.True);
        Assert.That(await Run(pair, () => db.UpdateMentorTicketAsync(new WolfgateMentorTicket { Id = -1 })), Is.False);

        var stored = await Run(pair, () => db.GetMentorTicketAsync(id));
        Assert.That(stored, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(stored!.CharacterName, Is.EqualTo("Ada Vance"));
            Assert.That(stored.Topic, Is.EqualTo("ships"));
            Assert.That(stored.Newbie, Is.True);
            Assert.That(stored.ClaimedBy, Is.EqualTo(mentor));
            Assert.That(stored.CloseReason, Is.EqualTo("resolved"));
            Assert.That(stored.Rating, Is.EqualTo(1));
            Assert.That(stored.RatingComment, Is.EqualTo("Thanks!"));
            Assert.That(stored.OpenedAt, Is.EqualTo(opened).Within(Slack));
            Assert.That(stored.ClosedAt, Is.EqualTo(opened.AddMinutes(3)).Within(Slack));
            Assert.That(stored.Messages.Select(m => m.Text),
                Is.EqualTo(new[] { "How do I dock?", "Open the shuttle console.", "Then pick a port.", "Resolved." }));
            Assert.That(stored.Messages[3].SenderUserId, Is.Null);
            Assert.That(stored.Messages[0].SentAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        });

        // Filters: by player, by mentor (claimed or posted in), by date.
        var byPlayer = await Ids(pair, db, player, null, null);
        var byClaim = await Ids(pair, db, null, mentor, null);
        var byPost = await Ids(pair, db, null, helper, null);
        var byStranger = await Ids(pair, db, null, Guid.NewGuid(), null);
        var byLaterDate = await Ids(pair, db, player, null, DateTime.UtcNow.AddMinutes(1));
        Assert.Multiple(() =>
        {
            Assert.That(byPlayer, Is.EqualTo(new[] { id }));
            Assert.That(byClaim, Is.EqualTo(new[] { id }), "Claimed.");
            Assert.That(byPost, Is.EqualTo(new[] { id }), "Posted in.");
            Assert.That(byStranger, Is.Empty);
            Assert.That(byLaterDate, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TranscriptsExpireButRowsStay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = pair.Server.ResolveDependency<IServerDbManager>();

        var player = Guid.NewGuid();
        var old = new DateTime(2001, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var recent = DateTime.UtcNow;

        var oldId = await AddClosed(pair, db, player, old);
        var recentId = await AddClosed(pair, db, player, recent);
        var openId = await Run(pair, () => db.AddMentorTicketAsync(new WolfgateMentorTicket
        {
            PlayerUserId = player,
            CharacterName = "Ada Vance",
            OpenedAt = old,
        }));
        await AddMessage(pair, db, openId, player, "player", "Still open.", old);

        var cleared = await Run(pair, () => db.ClearMentorTranscriptsAsync(new DateTime(2001, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.That(cleared, Is.EqualTo(2), "Only the old closed ticket's two messages.");

        var oldTicket = await Run(pair, () => db.GetMentorTicketAsync(oldId));
        var recentTicket = await Run(pair, () => db.GetMentorTicketAsync(recentId));
        var openTicket = await Run(pair, () => db.GetMentorTicketAsync(openId));
        Assert.Multiple(() =>
        {
            Assert.That(oldTicket!.Messages, Has.Count.EqualTo(2), "The rows stay for the numbers.");
            Assert.That(oldTicket.Messages.Select(m => m.Text), Is.All.Empty);
            Assert.That(oldTicket.Messages.Select(m => m.SenderUserId), Is.All.EqualTo(player));
            Assert.That(oldTicket.RatingComment, Is.Null);
            Assert.That(oldTicket.Rating, Is.EqualTo(-1), "The rating itself is kept.");
            Assert.That(recentTicket!.Messages.Select(m => m.Text), Is.All.Not.Empty);
            Assert.That(recentTicket.RatingComment, Is.EqualTo("Meh."));
            Assert.That(openTicket!.Messages.Single().Text, Is.EqualTo("Still open."));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CannedAnswersRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = pair.Server.ResolveDependency<IServerDbManager>();

        var editor = Guid.NewGuid();
        var title = $"Docking {Guid.NewGuid():N}";

        var id = await Run(pair, () => db.SaveMentorCannedAsync(new WolfgateMentorCanned
        {
            Title = title,
            Text = "See the docking guide.",
            GuideEntry = "Docking",
            UpdatedBy = editor,
            UpdatedAt = DateTime.UtcNow,
        }));
        Assert.That(id, Is.GreaterThan(0));

        var canned = (await Run(pair, () => db.GetMentorCannedAsync())).Single(c => c.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(canned.Title, Is.EqualTo(title));
            Assert.That(canned.GuideEntry, Is.EqualTo("Docking"));
            Assert.That(canned.UpdatedBy, Is.EqualTo(editor));
            Assert.That(canned.UpdatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        });

        canned.Text = "Open the shuttle console, then pick a port.";
        canned.GuideEntry = null;
        Assert.That(await Run(pair, () => db.SaveMentorCannedAsync(canned)), Is.EqualTo(id));

        var edited = (await Run(pair, () => db.GetMentorCannedAsync())).Single(c => c.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(edited.Text, Does.StartWith("Open the shuttle console"));
            Assert.That(edited.GuideEntry, Is.Null);
        });

        Assert.That(await Run(pair, () => db.DeleteMentorCannedAsync(id)), Is.True);
        Assert.That(await Run(pair, () => db.DeleteMentorCannedAsync(id)), Is.False);
        Assert.That((await Run(pair, () => db.GetMentorCannedAsync())).Select(c => c.Id), Does.Not.Contain(id));

        await pair.CleanReturnAsync();
    }

    private static async Task<int> AddClosed(TestPair pair, IServerDbManager db, Guid player, DateTime when)
    {
        var id = await Run(pair, () => db.AddMentorTicketAsync(new WolfgateMentorTicket
        {
            PlayerUserId = player,
            CharacterName = "Ada Vance",
            OpenedAt = when,
            ClosedAt = when.AddMinutes(5),
            CloseReason = "resolved",
            Rating = -1,
            RatingComment = "Meh.",
        }));

        await AddMessage(pair, db, id, player, "player", "Question.", when);
        await AddMessage(pair, db, id, player, "player", "Never mind.", when.AddMinutes(1));
        return id;
    }

    private static Task<int> AddMessage(
        TestPair pair,
        IServerDbManager db,
        int ticket,
        Guid? sender,
        string role,
        string text,
        DateTime sent)
    {
        return Run(pair, () => db.AddMentorTicketMessageAsync(new WolfgateMentorTicketMessage
        {
            TicketId = ticket,
            SenderUserId = sender,
            SenderRole = role,
            Text = text,
            SentAt = sent,
        }));
    }

    private static async Task<int[]> Ids(TestPair pair, IServerDbManager db, Guid? player, Guid? mentor, DateTime? since)
    {
        var tickets = await Run(pair, () => db.GetMentorTicketsAsync(player, mentor, since, 50));
        return tickets.Select(t => t.Id).ToArray();
    }

    /// <summary>Starts a database call on the server thread and ticks until it finishes.</summary>
    private static async Task<T> Run<T>(TestPair pair, Func<Task<T>> start)
    {
        Task<T> task = null!;
        await pair.Server.WaitPost(() => task = start());
        for (var i = 0; i < 600 && !task.IsCompleted; i++)
        {
            await pair.RunTicksSync(1);
            await Task.Delay(5);
        }

        Assert.That(task.IsCompleted, Is.True, "The database call never finished.");
        return await task;
    }
}
