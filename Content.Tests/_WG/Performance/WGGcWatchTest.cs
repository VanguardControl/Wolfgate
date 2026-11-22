using System;
using Content.Server._WG.Performance;
using NUnit.Framework;

namespace Content.Tests._WG.Performance;

/// <summary>Checks the GC watch reads collections and pauses, and says whether a stall was the collector.</summary>
[TestFixture]
[TestOf(typeof(WGGcWatchSystem))]
public sealed class WGGcWatchTest
{
    [Test]
    public void ReadSeesAFullCollection()
    {
        var before = WGGcWatchSystem.Read();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        var after = WGGcWatchSystem.Read();

        Assert.That(after.Gen2, Is.GreaterThan(before.Gen2));
        Assert.That(after.Pause, Is.GreaterThanOrEqualTo(before.Pause));
        Assert.That(after.HeapBytes, Is.GreaterThan(0));
    }

    [Test]
    public void StallReportNamesTheCause()
    {
        var before = Reading(TimeSpan.Zero, 10);
        var gap = TimeSpan.FromSeconds(16);

        var collector = WGGcWatchSystem.StallReport(gap, before, Reading(TimeSpan.FromSeconds(15.9), 11));
        var other = WGGcWatchSystem.StallReport(gap, before, Reading(TimeSpan.FromMilliseconds(40), 10));

        Assert.That(collector, Does.Contain("mostly garbage collection").And.Contain("1 gen2"));
        Assert.That(other, Does.Contain("not garbage collection").And.Contain("0 gen2"));
    }

    private static WGGcReading Reading(TimeSpan pause, int gen2) =>
        new(pause, 0, 0, gen2, 0, 0, 0, 0, 0, 0, 0, 0);
}
