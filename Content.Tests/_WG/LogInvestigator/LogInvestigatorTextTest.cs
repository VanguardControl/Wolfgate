using System;
using System.Linq;
using Content.Server._WG.LogInvestigator;
using Content.Shared._WG.LogInvestigator;
using Content.Shared.Database;
using NUnit.Framework;

namespace Content.Tests._WG.LogInvestigator;

/// <summary>Reading entity ids, round times and runs of repeated logs, and finding names in an ahelp.</summary>
[TestFixture]
[TestOf(typeof(LogInvestigatorText))]
public sealed class LogInvestigatorTextTest
{
    private static readonly DateTime Start = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static LogRow Row(int id, LogType type, int seconds, params int[] refs)
    {
        return new LogRow(id, type, LogImpact.Medium, Start.AddSeconds(seconds), $"log {id}",
            refs.Select(r => new LogRef("k" + r, r, null, null, null, null, null, null)).ToArray(), null);
    }

    [Test]
    public void EntityIdsMapServerToNetIds()
    {
        var ids = LogInvestigatorText.EntityIds("Amelia (18204606/n18204606, Mob) hit LWC AKM (7.62) (18170413/n99, WeaponRifleAk)");
        Assert.That(ids, Is.EquivalentTo(new[]
        {
            new System.Collections.Generic.KeyValuePair<int, int>(18204606, 18204606),
            new System.Collections.Generic.KeyValuePair<int, int>(18170413, 99),
        }));
    }

    [Test]
    public void RepeatsFoldIntoRuns()
    {
        var rows = new[]
        {
            Row(1, LogType.BulletHit, 0, 10, 20),
            Row(2, LogType.BulletHit, 2, 20, 10),
            Row(3, LogType.BulletHit, 4, 10, 20),
            Row(4, LogType.BulletHit, 30, 10, 20), // too long after
            Row(5, LogType.MeleeHit, 31, 10, 20), // another type
            Row(6, LogType.MeleeHit, 32, 10, 30), // other entities
        };

        var runs = LogInvestigatorText.Collapse(rows, TimeSpan.FromSeconds(10));
        Assert.That(runs.Select(r => (r.Start, r.Count)), Is.EqualTo(new[] { (0, 3), (3, 1), (4, 1), (5, 1) }));
        Assert.That(LogInvestigatorText.Collapse(Array.Empty<LogRow>(), TimeSpan.FromSeconds(10)), Is.Empty);
    }

    [TestCase("", null)]
    [TestCase("5", 300.0)]
    [TestCase("1:30", 90.0)]
    [TestCase("2:00:05", 7205.0)]
    [TestCase("1:2:3:4", null)]
    [TestCase("abc", null)]
    public void RoundTimesParse(string text, double? seconds)
    {
        Assert.That(LogInvestigatorText.ParseRoundTime(text), Is.EqualTo(seconds));
    }

    [Test]
    public void RoundTimesFormat()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LogInvestigatorText.RoundTime(TimeSpan.FromSeconds(7205)), Is.EqualTo("2:00:05"));
            Assert.That(LogInvestigatorText.RoundTime(TimeSpan.FromSeconds(-5)), Is.EqualTo("0:00:00"));
        });
    }

    [TestCase("Junal-Tar injected Maisie at the drydock", "Maisie", true)]
    [TestCase("maisie McMillan was there", "Maisie", true)]
    [TestCase("Maisiekins said hi", "Maisie", false)]
    [TestCase("the vox shot me", "vo", false)]
    public void NamesAreFoundAsWholeWords(string text, string name, bool found)
    {
        Assert.That(WGInvestigateCommand.Mentions(text, name), Is.EqualTo(found));
    }
}
