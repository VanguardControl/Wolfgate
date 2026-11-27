#nullable enable
using System.Linq;
using Content.Server._NF.GameRule;
using Content.Server._WG.PoiArrivals;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared.Maps;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.PoiArrivals;

/// <summary>
/// Arriving points of interest are held back at round start for 30 to 90 minutes, then load as their station on a holding
/// map and jump into the sector.
/// </summary>
[TestFixture]
[TestOf(typeof(WGPoiArrivalSystem))]
public sealed class PoiArrivalTest
{
    private const string Poi = "Sevastopol";
    private const int MaxSeconds = 60;

    [Test]
    public async Task ArrivalsNameRealPois()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            foreach (var arrival in protos.EnumeratePrototypes<WGPoiArrivalPrototype>())
            {
                Assert.That(protos.HasIndex<PointOfInterestPrototype>(arrival.ID), $"{arrival.ID} is no point of interest.");
                Assert.That(protos.HasIndex<GameMapPrototype>(arrival.ID), $"{arrival.ID} has no game map for its station.");
                Assert.That(arrival.MinDelay, Is.LessThanOrEqualTo(arrival.MaxDelay), $"{arrival.ID} arrives before it can.");
            }

            foreach (var id in new[] { "AnomalousLab", "Sevastopol" })
            {
                var arrival = protos.Index<WGPoiArrivalPrototype>(id);
                Assert.That(arrival.MinDelay, Is.EqualTo(TimeSpan.FromMinutes(30)));
                Assert.That(arrival.MaxDelay, Is.EqualTo(TimeSpan.FromMinutes(90)));
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
        var ticker = entMan.System<GameTicker>();
        var stations = entMan.System<StationSystem>();
        var xforms = entMan.System<SharedTransformSystem>();

        EntityUid grid = default;
        EntityUid holding = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.DefaultMap, Is.Not.EqualTo(MapId.Nullspace), "No sector map to arrive on.");

            Assert.That(arrivals.Defer(protos.Index<PointOfInterestPrototype>(Poi)));
            Assert.That(arrivals.Defer(protos.Index<PointOfInterestPrototype>("Lancelot")), Is.False,
                "A POI without an arrival was held back.");

            var due = arrivals.Due(Poi);
            Assert.That(due, Is.Not.Null);
            Assert.That(due!.Value, Is.InRange(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(90)));

            Assert.That(arrivals.WarpIn(Poi));
            Assert.That(arrivals.Due(Poi), Is.Null, "The POI is still pending after warping in.");
            Assert.That(arrivals.WarpIn(Poi), Is.False, "The POI warped in twice.");

            var query = entMan.EntityQueryEnumerator<WGPoiArrivalComponent>();
            Assert.That(query.MoveNext(out grid, out var arrival), "No grid is on its way.");
            holding = arrival!.Holding!.Value;

            Assert.That(entMan.HasComponent<FTLComponent>(grid), "The POI did not jump in.");
            Assert.That(entMan.GetComponent<TransformComponent>(grid).MapUid, Is.EqualTo(holding),
                "The POI should spool up on its holding map.");
            Assert.That(stations.GetOwningStation(grid), Is.Not.Null, "The POI was not set up as a station.");
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
            Assert.That(entMan.GetComponent<TransformComponent>(grid).MapID, Is.EqualTo(ticker.DefaultMap),
                "The POI arrived somewhere other than the sector map.");
            Assert.That(xforms.GetWorldPosition(grid).Length(), Is.GreaterThan(1000f), "The POI arrived at the sector core.");
            Assert.That(entMan.Deleted(holding), "The holding map was left behind.");

            var station = stations.GetOwningStation(grid);
            Assert.That(station, Is.Not.Null);
            Assert.That(entMan.GetComponent<MetaDataComponent>(station!.Value).EntityName,
                Is.EqualTo(protos.Index<PointOfInterestPrototype>(Poi).Name));
        });
    }
}
