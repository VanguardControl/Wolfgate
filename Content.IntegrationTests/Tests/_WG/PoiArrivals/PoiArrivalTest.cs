#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._NF.GameRule;
using Content.Server._WG.Contracts;
using Content.Server._WG.PoiArrivals;
using Content.Server.Station.Systems;
using Content.Shared._WG.PoiArrivals;
using Content.Shared.Maps;
using Content.Shared.Shuttles.Components;
using Robust.Server.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WG.PoiArrivals;

/// <summary>
/// Arriving points of interest are held back at round start for 15 to 45 minutes, then load as their station on a holding
/// map and jump into the sector; their contractors post only once they have arrived.
/// </summary>
[TestFixture]
[TestOf(typeof(WGPoiArrivalSystem))]
public sealed class PoiArrivalTest
{
    private const string Poi = "Sevastopol";
    private const string Contractor = "WGContractorSevastopol";
    private const string InboundContract = "WGContractSevDataInbound";
    private const string Board = "WGContractBoard";
    private const int MaxSeconds = 60;

    [Test]
    public async Task ArrivalsNameRealPois()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();
        var arrivals = pair.Server.EntMan.System<WGPoiArrivalSystem>();

        await pair.Server.WaitAssertion(() =>
        {
            var all = protos.EnumeratePrototypes<WGPoiArrivalPrototype>().ToList();
            foreach (var arrival in all)
            {
                Assert.That(protos.HasIndex<PointOfInterestPrototype>(arrival.Poi), $"{arrival.ID}: {arrival.Poi} is no point of interest.");
                Assert.That(protos.HasIndex<GameMapPrototype>(arrival.Poi), $"{arrival.ID}: {arrival.Poi} has no game map for its station.");
                Assert.That(arrival.MinDelay, Is.LessThanOrEqualTo(arrival.MaxDelay), $"{arrival.ID} arrives before it can.");
                Assert.That(all.Count(other => other.Poi == arrival.Poi), Is.EqualTo(1), $"{arrival.Poi} has more than one arrival.");
            }

            foreach (var poi in new[] { "AnomalousLab", "Sevastopol" })
            {
                var arrival = arrivals.ArrivalOf(poi);
                Assert.That(arrival, Is.Not.Null, $"{poi} doesn't arrive mid-shift.");
                Assert.That(arrival!.MinDelay, Is.EqualTo(TimeSpan.FromMinutes(15)));
                Assert.That(arrival.MaxDelay, Is.EqualTo(TimeSpan.FromMinutes(45)));
            }
        });
    }

    [Test]
    public async Task HeldBackPoiJumpsIn()
    {
        // Dirty: the test adds a station the next test should not inherit.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var arrivals = entMan.System<WGPoiArrivalSystem>();
        var stations = entMan.System<StationSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var contracts = entMan.System<WGContractSystem>();
        var console = server.ResolveDependency<IServerConsoleHost>();

        // The test pool runs no round, so the test makes the sector map the adventure rule would pass in.
        var sector = await pair.CreateTestMap();

        EntityUid grid = default;
        EntityUid holding = default;

        await server.WaitAssertion(() =>
        {
            // A contract board, so the only thing between Sevastopol and its contracts is the station being there.
            BoardStation(server, sector.MapId);
            Assert.That(contracts.ContractorHomes().ContainsKey(Contractor), Is.False,
                "Sevastopol's contractor has a home before Sevastopol arrived.");
            Assert.That(contracts.PostOffer(InboundContract), Is.Null, "Sevastopol posted before it arrived.");

            Assert.That(arrivals.Defer(protos.Index<PointOfInterestPrototype>(Poi), sector.MapId));
            Assert.That(arrivals.Defer(protos.Index<PointOfInterestPrototype>("Lancelot"), sector.MapId), Is.False,
                "A POI without an arrival was held back.");

            var due = arrivals.Due(Poi);
            Assert.That(due, Is.Not.Null);
            Assert.That(due!.Value, Is.InRange(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(45)));
            Assert.That(arrivals.PendingArrivals().Select(entry => entry.Id), Does.Contain(Poi));

            // Admins force it in ahead of schedule.
            console.ExecuteCommand("wg_poiarrivals sevastopol");
            Assert.That(arrivals.Due(Poi), Is.Null, "The POI is still pending after being forced in.");
            Assert.That(arrivals.WarpIn(Poi), Is.False, "The POI warped in twice.");

            var query = entMan.EntityQueryEnumerator<WGPoiArrivalComponent>();
            Assert.That(query.MoveNext(out grid, out var arrival), "No grid is on its way.");
            holding = arrival!.Holding!.Value;

            Assert.That(entMan.HasComponent<FTLComponent>(grid), "The POI did not jump in.");
            Assert.That(entMan.GetComponent<TransformComponent>(grid).MapUid, Is.EqualTo(holding),
                "The POI should spool up on its holding map.");
            Assert.That(stations.GetOwningStation(grid), Is.Not.Null, "The POI was not set up as a station.");
            Assert.That(entMan.HasComponent<FTLDestinationComponent>(holding), Is.False,
                "Pilots can jump to the holding map, which is deleted on arrival.");
            Assert.That(contracts.ContractorHomes().ContainsKey(Contractor), Is.False,
                "Sevastopol's contractor found its station while it was still jumping in.");
        });

        var arrived = false;
        for (var second = 0; second < MaxSeconds && !arrived; second++)
        {
            await pair.RunTicksSync(60);
            await server.WaitPost(() => arrived = !entMan.HasComponent<WGPoiArrivalComponent>(grid));
        }

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(arrived, "The POI never arrived.");
            Assert.That(entMan.GetComponent<TransformComponent>(grid).MapID, Is.EqualTo(sector.MapId),
                "The POI arrived somewhere other than the sector map.");
            Assert.That(xforms.GetWorldPosition(grid).Length(), Is.GreaterThan(1000f), "The POI arrived at the sector core.");
            Assert.That(entMan.Deleted(holding), "The holding map was left behind.");

            var station = stations.GetOwningStation(grid);
            Assert.That(station, Is.Not.Null);
            Assert.That(entMan.GetComponent<MetaDataComponent>(station!.Value).EntityName,
                Is.EqualTo(protos.Index<PointOfInterestPrototype>(Poi).Name));

            Assert.That(contracts.ContractorHomes().GetValueOrDefault(Contractor), Is.EqualTo(station.Value),
                "Sevastopol's contractor did not find its station after it arrived.");
            var offer = contracts.PostOffer(InboundContract);
            Assert.That(offer, Is.Not.Null, "Sevastopol posted nothing after it arrived.");
            Assert.That(offer!.Destination, Is.EqualTo(station.Value), "Sevastopol's inbound run isn't bound for Sevastopol.");
        });
    }

    /// <summary>A station with the public contract board on it, near the sector core.</summary>
    private static void BoardStation(RobustIntegrationTest.ServerIntegrationInstance server, MapId map)
    {
        var entMan = server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(map);
        for (var x = 0; x < 6; x++)
        {
            for (var y = 0; y < 6; y++)
            {
                maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), new Tile(1));
            }
        }

        entMan.System<SharedTransformSystem>().SetLocalPosition(grid.Owner, new Vector2(0f, 300f));
        var proto = server.ProtoMan.Index<GameMapPrototype>("Frontier");
        entMan.System<StationSystem>().InitializeNewStation(proto.Stations.Values.First(), new[] { grid.Owner }, proto.MapName);
        entMan.SpawnEntity(Board, new EntityCoordinates(grid.Owner, 2.5f, 2.5f));
    }
}
