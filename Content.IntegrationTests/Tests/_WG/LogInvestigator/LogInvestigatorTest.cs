#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.LogInvestigator;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._WG.LogInvestigator;
using Content.Shared.Database;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.LogInvestigator;

/// <summary>The investigator's query, the positions written into logs, and its scripted reports.</summary>
[TestFixture]
public sealed class LogInvestigatorTest
{
    private const int MaxWaitTicks = 400;

    [Test]
    public async Task EveryLogTypeHasOneCategory()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        var categorized = proto.EnumeratePrototypes<WGLogCategoryPrototype>().SelectMany(c => c.Types).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(Enum.GetValues<LogType>().Except(categorized), Is.Empty, "Every log type needs a category in log_categories.yml.");
            Assert.That(categorized.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key), Is.Empty, "A log type is in two categories.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task QueryFindsIdsTextAndPositions()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            AdminLogsEnabled = true,
            DummyTicker = false,
            Connected = true,
            Dirty = true,
        });
        var server = pair.Server;
        var logs = server.ResolveDependency<IAdminLogManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var ticker = server.System<GameTicker>();
        var map = await pair.CreateTestMap();

        EntityUid attacker = default, victim = default;
        await server.WaitPost(() =>
        {
            attacker = server.EntMan.SpawnEntity(null, new EntityCoordinates(map.Grid, 1.5f, 2.5f));
            victim = server.EntMan.SpawnEntity(null, new EntityCoordinates(map.Grid, 3.5f, 2.5f));
            for (var i = 0; i < 3; i++)
            {
                logs.Add(LogType.MeleeHit, LogImpact.Medium,
                    $"{server.EntMan.ToPrettyString(attacker):actor} wgtest STRUCK {server.EntMan.ToPrettyString(victim):subject} number {i}");
            }
        });

        var query = new LogQuery { RoundId = ticker.RoundId, Entities = new[] { (int) attacker }, Text = "wgtest struck", Ascending = true };
        List<LogRow> rows = new();
        for (var i = 0; i < MaxWaitTicks && rows.Count < 3; i++)
        {
            await pair.RunTicksSync(5);
            (rows, _, _) = await db.GetInvestigatorLogsAsync(query, null, null, 10, false);
        }

        Assert.That(rows, Has.Count.EqualTo(3), "The three logs should be found by entity id and text, ignoring case.");
        var actor = rows[0].Refs.Single(r => r.Key == "actor");
        Assert.Multiple(() =>
        {
            Assert.That(actor.Id, Is.EqualTo((int) attacker));
            Assert.That(actor.Map, Is.Not.Null, "MeleeHit records positions.");
            Assert.That(actor.Grid, Is.EqualTo((int) map.Grid.Owner));
            Assert.That(rows[0].Refs.Any(r => r.Key == "subject" && r.Id == (int) victim));
        });

        // Paging by the last id never repeats a log.
        var seen = new List<int>();
        int? after = null;
        for (var page = 0; page < 4; page++)
        {
            var (pageRows, hasMore, _) = await db.GetInvestigatorLogsAsync(query, null, after, 1, false);
            seen.AddRange(pageRows.Select(r => r.Id));
            if (!hasMore)
                break;
            after = pageRows[^1].Id;
        }

        Assert.That(seen, Is.EqualTo(rows.Select(r => r.Id)));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReportsLineUpWhatTheLogsSay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var start = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var carol = Guid.NewGuid();

        LogRef Ref(string key, int id, string name, Guid? player = null, int? grid = null)
        {
            return new LogRef(key, id, name, player, 1, grid, 10, 10);
        }

        await server.WaitAssertion(() =>
        {
            var combat = new List<LogRow>
            {
                // Bob's turret hits Alice; Bob fired the ship's guns a second earlier.
                new(1, LogType.BulletHit, LogImpact.High, start.AddSeconds(100), "turret hit Alice",
                    new[] { Ref("source", 50, "Hydra battery", grid: 900), Ref("target", 10, "Alice", alice) }, 40),
                new(2, LogType.MeleeHit, LogImpact.High, start.AddSeconds(110), "Alice hit Bob",
                    new[] { Ref("actor", 10, "Alice", alice), Ref("subject", 20, "Bob", bob), Ref("tool", 60, "knife") }, 15),
            };
            var guns = new List<LogRow>
            {
                new(3, LogType.ShipgunFired, LogImpact.High, start.AddSeconds(99), "Bob fired",
                    new[] { Ref("user", 20, "Bob", bob), Ref("entity", 900, "Femur") }, null),
            };

            var hits = LogReports.Hits(combat, guns);
            Assert.That(hits, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(hits[0].AttackerPlayer, Is.EqualTo(bob), "The turret hit should be credited to its gunner.");
                Assert.That(hits[1].Weapon, Is.EqualTo("knife"));
            });

            var deaths = new List<LogRow>
            {
                new(4, LogType.Damaged, LogImpact.Medium, start.AddSeconds(120), "Alice state changed from Critical to Dead", Array.Empty<LogRef>(), null),
            };
            var report = LogReports.Encounter(hits, deaths, new List<LogRow>(), start, null);
            var first = report.Sections.First().Lines.Single();
            Assert.Multiple(() =>
            {
                Assert.That(first, Does.Contain("Femur").Or.Contain("Bob"), "Bob struck first, through his turret.");
                Assert.That(first, Does.StartWith("0:01:40"));
                Assert.That(report.Sections.SelectMany(s => s.Lines).Any(l => l.Contains("to Dead")));
            });

            var point = new LogPoint(1, 900, 0, 0, start.AddSeconds(100));
            var positions = new List<WolfgatePlayerPosition>
            {
                new() { PlayerUserId = alice, MapId = 1, GridId = 900, X = 3, Y = 4, SampledAt = start.AddSeconds(95), Alive = true, EntityName = "Alice" },
                new() { PlayerUserId = alice, MapId = 1, GridId = 900, X = 30, Y = 40, SampledAt = start.AddSeconds(105), Alive = true, EntityName = "Alice" },
                new() { PlayerUserId = bob, MapId = 1, GridId = 901, X = 100, Y = 0, SampledAt = start.AddSeconds(100), Alive = true, EntityName = "Bob" },
                new() { PlayerUserId = carol, MapId = 2, GridId = null, X = 0, Y = 0, SampledAt = start.AddSeconds(100), Alive = true, EntityName = "Carol" },
            };
            var nearby = LogReports.Nearby(point, 15, positions, new Dictionary<Guid, string> { [alice] = "alice" }, new HashSet<Guid> { alice });
            Assert.That(nearby, Has.Count.EqualTo(1), "Bob was too far and off the grid; Carol was on another map.");
            Assert.Multiple(() =>
            {
                Assert.That(nearby[0].Player, Is.EqualTo(alice));
                Assert.That(nearby[0].Distance, Is.EqualTo(5f));
                Assert.That(nearby[0].SameGrid);
                Assert.That(nearby[0].Spoke);
            });
        });

        await pair.CleanReturnAsync();
    }
}
