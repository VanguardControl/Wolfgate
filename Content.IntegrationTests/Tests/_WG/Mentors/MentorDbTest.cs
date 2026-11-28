#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Database;

namespace Content.IntegrationTests.Tests._WG.Mentors;

/// <summary>Roster rows and applications round-trip through the database.</summary>
[TestFixture]
[TestOf(typeof(WolfgateMentor))]
public sealed class MentorDbTest
{
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

    [Test]
    public async Task RosterRowRoundTrips()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = pair.Server.ResolveDependency<IServerDbManager>();

        var user = Guid.NewGuid();
        var granter = Guid.NewGuid();
        var granted = DateTime.UtcNow.AddDays(-3);

        Assert.That(await Run(pair, () => db.GetMentorAsync(user)), Is.Null);

        await Run(pair, async () =>
        {
            await db.SaveMentorAsync(new WolfgateMentor
            {
                UserId = user,
                Tier = "trainee",
                State = "active",
                Source = "auto",
                GrantedAt = granted,
                GrantedBy = granter,
                LastActiveAt = granted,
            });
            return true;
        });

        var stored = await Run(pair, () => db.GetMentorAsync(user));
        Assert.That(stored, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(stored!.Tier, Is.EqualTo("trainee"));
            Assert.That(stored.Source, Is.EqualTo("auto"));
            Assert.That(stored.GrantedBy, Is.EqualTo(granter));
            Assert.That(stored.GrantedAt, Is.EqualTo(granted).Within(Slack));
            Assert.That(stored.GrantedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(stored.History, Is.EqualTo("[]"));
            Assert.That(stored.StaffNotes, Is.EqualTo(""));
            Assert.That(stored.PromotedAt, Is.Null);
        });

        // Saving again replaces the row rather than adding one.
        var promoted = DateTime.UtcNow;
        stored!.Tier = "mentor";
        stored.PromotedAt = promoted;
        stored.History = "[{\"what\":\"promoted\"}]";
        await Run(pair, async () =>
        {
            await db.SaveMentorAsync(stored);
            return true;
        });

        var active = DateTime.UtcNow.AddMinutes(1);
        Assert.That(await Run(pair, () => db.SetMentorLastActiveAsync(user, active)), Is.True);
        Assert.That(await Run(pair, () => db.SetMentorLastActiveAsync(Guid.NewGuid(), active)), Is.False);

        var roster = await Run(pair, () => db.GetMentorsAsync());
        var mine = roster.Where(m => m.UserId == user).ToList();
        Assert.That(mine, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(mine[0].Tier, Is.EqualTo("mentor"));
            Assert.That(mine[0].PromotedAt, Is.EqualTo(promoted).Within(Slack));
            Assert.That(mine[0].LastActiveAt, Is.EqualTo(active).Within(Slack));
            Assert.That(mine[0].History, Does.Contain("promoted"));
        });

        Assert.That(await Run(pair, () => db.DeleteMentorAsync(user)), Is.True);
        Assert.That(await Run(pair, () => db.DeleteMentorAsync(user)), Is.False);
        Assert.That(await Run(pair, () => db.GetMentorAsync(user)), Is.Null);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ApplicationsRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = pair.Server.ResolveDependency<IServerDbManager>();

        var user = Guid.NewGuid();
        var reviewer = Guid.NewGuid();

        var first = await Run(pair, () => db.AddMentorApplicationAsync(new WolfgateMentorApplication
        {
            UserId = user,
            CreatedAt = DateTime.UtcNow.AddDays(-40),
            PlaytimeMinutes = 6000,
            Answers = "{\"q1\":\"yes\"}",
            RecordSummary = "{\"spotless\":false}",
        }));

        var second = await Run(pair, () => db.AddMentorApplicationAsync(new WolfgateMentorApplication
        {
            UserId = user,
            CreatedAt = DateTime.UtcNow,
            PlaytimeMinutes = 6100,
        }));

        Assert.That(second, Is.GreaterThan(first));

        // A review writes the decision back.
        var pending = await Run(pair, () => db.GetMentorApplicationsAsync("pending", user));
        Assert.That(pending.Select(a => a.Id), Is.EqualTo(new[] { second, first }), "Newest first.");

        var denied = pending.Single(a => a.Id == first);
        var reapply = DateTime.UtcNow.AddDays(-10);
        denied.State = "denied";
        denied.ReviewedBy = reviewer;
        denied.ReviewedAt = DateTime.UtcNow.AddDays(-39);
        denied.Reason = "Not yet.";
        denied.ReapplyAfter = reapply;
        await Run(pair, async () =>
        {
            await db.UpdateMentorApplicationAsync(denied);
            return true;
        });

        var all = await Run(pair, () => db.GetMentorApplicationsAsync(null, user));
        var stored = all.Single(a => a.Id == first);
        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(2));
            Assert.That(stored.State, Is.EqualTo("denied"));
            Assert.That(stored.ReviewedBy, Is.EqualTo(reviewer));
            Assert.That(stored.Reason, Is.EqualTo("Not yet."));
            Assert.That(stored.ReapplyAfter, Is.EqualTo(reapply).Within(Slack));
            Assert.That(stored.Answers, Does.Contain("q1"));
            Assert.That(stored.PlaytimeMinutes, Is.EqualTo(6000));
            Assert.That(all.Single(a => a.Id == second).Answers, Is.EqualTo("{}"));
        });

        Assert.That(await Run(pair, () => db.GetMentorApplicationsAsync("pending", user)), Has.Count.EqualTo(1));
        Assert.That(await Run(pair, () => db.GetMentorApplicationsAsync(null, Guid.NewGuid())), Is.Empty);

        await pair.CleanReturnAsync();
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
