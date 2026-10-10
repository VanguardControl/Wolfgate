#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Content.Client._WF.SectorControl;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Sync: the server builds the territory and legend events, and the client turns them into what the map draws.</summary>
[TestOf(typeof(WFSectorSyncSystem))]
public sealed class WFSectorSyncTest : WFSectorTestBase
{
    private static readonly WFSectorCell A = new(4, -2);
    private static readonly WFSectorCell B = new(5, -2);
    private static readonly WFSectorCell C = new(-4, 2);
    private static readonly WFSectorCell D = new(-6, 3);

    /// <summary>The event carries the cell size, the factions used, every claim by faction index and every contest with its deadline.</summary>
    [Test]
    public async Task BuildCarriesClaimsContestsAndLegend()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var sync = Server.System<WFSectorSyncSystem>();
            var map = MapData.MapId;

            Assert.That(sync.Build(map).Claims, Is.Empty);
            Assert.That(territory.TryClaim(map, A, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, B, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, C, Rival, null), Is.True);
            var deadline = STiming.CurTime + TimeSpan.FromMinutes(3);
            Assert.That(territory.Contest(map, D, Rival, deadline), Is.True);

            var ev = sync.Build(map);
            Assert.That(ev.Map, Is.EqualTo(map));
            Assert.That(ev.CellSize, Is.EqualTo(territory.CellSize));
            Assert.That(ev.Factions, Is.EquivalentTo(new[] { Holder, Rival }));
            Assert.That(ev.Claims, Has.Count.EqualTo(3));
            Assert.That(ev.Factions[ev.Claims.Single(claim => claim.Cell == A).Faction], Is.EqualTo(Holder));
            Assert.That(ev.Factions[ev.Claims.Single(claim => claim.Cell == B).Faction], Is.EqualTo(Holder));
            Assert.That(ev.Factions[ev.Claims.Single(claim => claim.Cell == C).Faction], Is.EqualTo(Rival));
            var contest = ev.Contests.Single();
            Assert.That(contest.Cell, Is.EqualTo(D));
            Assert.That(ev.Factions[contest.Faction], Is.EqualTo(Rival));
            Assert.That(contest.Deadline, Is.EqualTo(deadline));

            Assert.That(sync.BuildStatus().Lines, Is.Empty);
            sync.SetStatus(Holder, "Test faction: Scattered");
            sync.SetStatus(Rival, "Rivals: here");
            Assert.That(sync.BuildStatus().Lines.Select(line => line.Faction), Is.EquivalentTo(new[] { Holder, Rival }));
            sync.SetStatus(Rival, null);
            Assert.That(sync.BuildStatus().Lines.Single(), Is.EqualTo(new WFSectorStatusLine(Holder, "Test faction: Scattered")));
            sync.SetStatus(Holder, string.Empty);
            Assert.That(sync.BuildStatus().Lines, Is.Empty, "An empty line takes the legend entry down.");
        });
    }

    /// <summary>A change reaches the client as drawable cells; the last claim going sends one empty update that clears them.</summary>
    [Test]
    public async Task ClientHearsTerritoryAndLegendAndClearsThem()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            Assert.That(territory.TryClaim(map, A, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, B, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, C, Rival, null), Is.True);
            Assert.That(territory.Contest(map, D, Rival, STiming.CurTime + TimeSpan.FromMinutes(3)), Is.True);
            Server.System<WFSectorSyncSystem>().SetStatus(Holder, "Test faction: Scattered");
        });
        await RunTicks(90);
        await Client.WaitAssertion(() =>
        {
            var client = Client.System<WFSectorClientSystem>();
            Assert.That(client.TryGetMap(MapData.MapId, out var view), Is.True, "The client has the map's territory.");
            Assert.That(view.CellSize, Is.EqualTo(3000f));
            Assert.That(view.Labels.Select(label => label.Text), Is.EquivalentTo(new[]
            {
                WFSectorHex.Callsign(A), WFSectorHex.Callsign(B), WFSectorHex.Callsign(C),
            }));
            Assert.That(view.Contests.Single().Deadline, Is.Not.EqualTo(TimeSpan.Zero));

            // Per faction, a fan of six triangles a cell, and a line pair for each edge facing a cell it does not hold.
            var fills = view.Fills.Select(fill => fill.Length).OrderBy(length => length).ToList();
            Assert.That(fills, Is.EqualTo(new[] { 3 * 6 * 1, 3 * 6 * 2 }));
            var edges = view.Edges.Select(edge => edge.Length).OrderBy(length => length).ToList();
            Assert.That(edges, Is.EqualTo(new[] { 12, 20 }), "A lone cell has six edges; two neighbours share one, ten between them.");

            Assert.That(client.Legend.Single().Text, Is.EqualTo("Test faction: Scattered"));
        });

        await Server.WaitAssertion(() =>
        {
            Server.System<WFSectorTerritorySystem>().Clear(MapData.MapId);
            Server.System<WFSectorSyncSystem>().SetStatus(Holder, null);
        });
        await RunTicks(90);
        await Client.WaitAssertion(() =>
        {
            var client = Client.System<WFSectorClientSystem>();
            Assert.That(client.TryGetMap(MapData.MapId, out _), Is.False, "The empty update clears the map.");
            Assert.That(client.Legend, Is.Empty);
        });
    }
}
