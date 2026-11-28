using System;
using Content.Server._WG.Mentors;
using Content.Shared._WG.Mentors;
using NUnit.Framework;

namespace Content.Tests._WG.Mentors;

/// <summary>Who may apply to be a mentor, who is approved at once, and when a Trainee is promoted.</summary>
[TestFixture]
[TestOf(typeof(MentorEligibility))]
public sealed class MentorEligibilityRulesTest
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    private static MentorRecord Clean()
    {
        return new MentorRecord { Playtime = TimeSpan.FromHours(100) };
    }

    [Test]
    public void HoursAreAHardLine()
    {
        var short1 = Clean();
        short1.Playtime = TimeSpan.FromHours(100) - TimeSpan.FromMinutes(1);

        Assert.Multiple(() =>
        {
            Assert.That(MentorEligibility.Check(short1, 100, Now).Blocks, Is.EqualTo(new[] { MentorBlock.Hours }));
            Assert.That(MentorEligibility.Check(Clean(), 100, Now).CanApply);
            Assert.That(MentorEligibility.Check(Clean(), 100, Now).Spotless);
        });
    }

    [Test]
    public void EachRecordCaseBlocks()
    {
        var banned = Clean();
        banned.ActiveBan = true;

        var mentorBanned = Clean();
        mentorBanned.MentorBanned = true;
        mentorBanned.MentorBanUntil = Now.AddDays(3);

        var permanent = Clean();
        permanent.MentorBanned = true;

        var recentBan = Clean();
        recentBan.RecentBans = 1;

        var seriousNote = Clean();
        seriousNote.SeriousNotes = 1;

        var waiting = Clean();
        waiting.ReapplyAfter = Now.AddDays(1);

        Assert.Multiple(() =>
        {
            Assert.That(MentorEligibility.Check(banned, 100, Now).Blocks, Does.Contain(MentorBlock.Ban));
            Assert.That(MentorEligibility.Check(mentorBanned, 100, Now).Blocks, Does.Contain(MentorBlock.MentorBan));
            Assert.That(MentorEligibility.Check(permanent, 100, Now).Blocks, Does.Contain(MentorBlock.MentorBan));
            Assert.That(MentorEligibility.Check(recentBan, 100, Now).Blocks, Does.Contain(MentorBlock.Record));
            Assert.That(MentorEligibility.Check(seriousNote, 100, Now).Blocks, Does.Contain(MentorBlock.Record));
            Assert.That(MentorEligibility.Check(waiting, 100, Now).Blocks, Does.Contain(MentorBlock.Reapply));
        });
    }

    [Test]
    public void PastDatesNoLongerBlock()
    {
        var expired = Clean();
        expired.MentorBanned = true;
        expired.MentorBanUntil = Now.AddMinutes(-1);
        expired.ReapplyAfter = Now.AddMinutes(-1);

        Assert.That(MentorEligibility.Check(expired, 100, Now).CanApply);
    }

    [Test]
    public void MinorNotesAndPastRevocationGoToReview()
    {
        var minor = Clean();
        minor.MinorNotes = 2;

        var revoked = Clean();
        revoked.RevokedBefore = true;

        Assert.Multiple(() =>
        {
            Assert.That(MentorEligibility.Check(minor, 100, Now).CanApply);
            Assert.That(MentorEligibility.Check(minor, 100, Now).Spotless, Is.False);
            Assert.That(MentorEligibility.Check(revoked, 100, Now).CanApply);
            Assert.That(MentorEligibility.Check(revoked, 100, Now).Spotless, Is.False);
        });
    }

    [Test]
    public void Promotion()
    {
        var days = TimeSpan.FromDays(14);
        Assert.Multiple(() =>
        {
            Assert.That(MentorEligibility.Promotable(10, 5, 4, days, 10, 0.8f, 14), "Ten resolved, 80 % helpful, two weeks.");
            Assert.That(MentorEligibility.Promotable(9, 5, 5, days, 10, 0.8f, 14), Is.False, "Too few resolved.");
            Assert.That(MentorEligibility.Promotable(10, 5, 3, days, 10, 0.8f, 14), Is.False, "Rated too low.");
            Assert.That(MentorEligibility.Promotable(10, 5, 5, days - TimeSpan.FromHours(1), 10, 0.8f, 14), Is.False,
                "Not long enough on the roster.");
            Assert.That(MentorEligibility.Promotable(10, 0, 0, days, 10, 0.8f, 14), "No ratings to fall short of.");
        });
    }
}
