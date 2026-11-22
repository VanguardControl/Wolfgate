using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Mentors;
using Content.Server.Database;
using Content.Shared._WG.Mentors;
using NUnit.Framework;

namespace Content.Tests._WG.Mentors;

/// <summary>Leaderboard credit, which closes count, ranking and its ties, and period edges.</summary>
[TestFixture]
[TestOf(typeof(MentorBoard))]
public sealed class MentorBoardRulesTest
{
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly DateTime Opened = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private static MentorBoardTicket Ticket(Guid? claimer, string reason, int? rating = null, int answerSeconds = 60,
        params Guid[] responders)
    {
        return new MentorBoardTicket(claimer, reason, Opened, Opened.AddSeconds(answerSeconds), rating,
            responders.Length > 0 ? responders.ToList() : claimer is { } c ? new List<Guid> { c } : new List<Guid>(),
            new List<Guid>(), new List<Guid>());
    }

    [Test]
    public void ResolveGoesToTheHolderAndAssistsToTheRest()
    {
        var stats = MentorBoard.Compute(new[] { Ticket(A, "resolved", 1, 60, A, B) });
        var a = stats.Single(s => s.Mentor == A);
        var b = stats.Single(s => s.Mentor == B);
        Assert.Multiple(() =>
        {
            Assert.That(a.Resolved, Is.EqualTo(1));
            Assert.That(a.Helpful, Is.EqualTo(1));
            Assert.That(a.Assists, Is.Zero);
            Assert.That(b.Resolved, Is.Zero);
            Assert.That(b.Assists, Is.EqualTo(1));
        });
    }

    [Test]
    public void WhichClosesCount()
    {
        var answeredEnd = Ticket(A, "player");
        var unansweredEnd = new MentorBoardTicket(A, "player", Opened, null, null, new List<Guid>(), new List<Guid>(), new List<Guid>());
        var stats = MentorBoard.Compute(new[]
        {
            answeredEnd, unansweredEnd, Ticket(A, "idle"), Ticket(A, "round_end"), Ticket(A, "fallback"), Ticket(A, "escalated"),
        });

        var a = stats.Single(s => s.Mentor == A);
        Assert.Multiple(() =>
        {
            Assert.That(a.Resolved, Is.EqualTo(1), "Only the end after an answer counts.");
            Assert.That(a.Escalations, Is.EqualTo(1));
        });
    }

    [Test]
    public void RankedByHelpfulThenResolvedThenSpeed()
    {
        var stats = MentorBoard.Compute(new[]
        {
            // A: two resolved, none helpful. B: one resolved, helpful. C: one resolved, helpful, but slower.
            Ticket(A, "resolved"), Ticket(A, "resolved"),
            Ticket(B, "resolved", 1, 30),
            Ticket(C, "resolved", 1, 300),
        });

        Assert.That(stats.Select(s => s.Mentor), Is.EqualTo(new[] { B, C, A }));
    }

    [Test]
    public void WatchesAndCritterVisitsCount()
    {
        var ticket = new MentorBoardTicket(A, "resolved", Opened, Opened.AddSeconds(5), null, new List<Guid> { A },
            new List<Guid> { A, B }, new List<Guid> { B });
        var stats = MentorBoard.Compute(new[] { ticket });
        Assert.Multiple(() =>
        {
            Assert.That(stats.Single(s => s.Mentor == A).Watches, Is.EqualTo(1));
            Assert.That(stats.Single(s => s.Mentor == B).Watches, Is.EqualTo(1));
            Assert.That(stats.Single(s => s.Mentor == B).CritterVisits, Is.EqualTo(1));
        });
    }

    [Test]
    public void MedianFirstAnswer()
    {
        var stats = MentorBoard.Compute(new[] { Ticket(A, "resolved", null, 10), Ticket(A, "resolved", null, 30) });
        Assert.That(stats.Single().MedianFirstAnswer, Is.EqualTo(20));
    }

    [Test]
    public void PeriodsStartOnMondayAndTheFirst()
    {
        var thursday = new DateTime(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);
        Assert.Multiple(() =>
        {
            Assert.That(MentorBoard.Since(MentorBoardPeriod.Week, thursday), Is.EqualTo(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(MentorBoard.Since(MentorBoardPeriod.Week, new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc)),
                Is.EqualTo(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc)), "Monday is its own week.");
            Assert.That(MentorBoard.Since(MentorBoardPeriod.Week, new DateTime(2026, 11, 22, 23, 0, 0, DateTimeKind.Utc)),
                Is.EqualTo(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc)), "Sunday ends the week.");
            Assert.That(MentorBoard.Since(MentorBoardPeriod.Month, thursday), Is.EqualTo(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(MentorBoard.Since(MentorBoardPeriod.All, thursday), Is.Null);
        });
    }
}
