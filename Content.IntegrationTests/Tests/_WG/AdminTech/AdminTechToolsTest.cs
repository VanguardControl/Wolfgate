#nullable enable
using Content.IntegrationTests.Tests._WG.AdminObserver;
using Content.Server._WG.AdminTech;
using Content.Server.Mind;
using Content.Server.Wires;
using Content.Shared._WG.AdminObserver;
using Content.Shared._WG.AdminTech;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.AdminTech;

/// <summary>The wire legend and Return.</summary>
[TestFixture]
[TestOf(typeof(WGReturnSystem))]
public sealed class AdminTechToolsTest
{
    [Test]
    public async Task WireLegendIsForTheHolderOfTheSubspaceMultitool()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var legend = server.System<WGWireLegendSystem>();
        var hands = server.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var airlock = entMan.SpawnEntity("Airlock", map.GridCoords);
            var wires = (airlock, entMan.GetComponent<WiresComponent>(airlock));
            var technician = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var engineer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(technician, entMan.SpawnEntity("WGAdminTechMultitool", map.GridCoords)));
            Assert.That(hands.TryPickupAnyHand(engineer, entMan.SpawnEntity("Multitool", map.GridCoords)));

            Assert.That(legend.TryGetLegend(wires, technician, out var entries), "The subspace multitool's holder gets the legend.");
            Assert.Multiple(() =>
            {
                Assert.That(entries, Is.Not.Empty);
                Assert.That(entries!.TrueForAll(e => e.Name.Length > 0), "Every listed wire says what it does.");
                Assert.That(legend.TryGetLegend(wires, engineer, out _), Is.False, "An ordinary multitool shows nothing.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReturnGoesBackToTheEarlierBodyOrAnObserver()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var returns = server.System<WGReturnSystem>();
        var minds = server.System<MindSystem>();
        var map = await pair.CreateTestMap();
        var session = await AdminKitFixture.Admin(pair);
        var earlier = await AdminKitFixture.Living(pair, map.GridCoords);

        // With a body to go back to.
        EntityUid technician = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(returns.EarlierBody(session), Is.EqualTo(earlier));
            technician = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entMan.HasComponent<WGReturnToObserverComponent>(technician), Is.False, "Return is only there once given.");

            minds.ControlMob(session.UserId, technician);
            returns.Grant(technician, earlier);
            var comp = entMan.GetComponent<WGReturnToObserverComponent>(technician);
            Assert.That(entMan.EntityExists(comp.ActionEntity), "Return is an action on the body.");
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() => returns.Return((technician, entMan.GetComponent<WGReturnToObserverComponent>(technician))));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(minds.TryGetMind(session, out _, out var mind));
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(technician), "The spawned body is deleted.");
                Assert.That(mind!.OwnedEntity, Is.EqualTo(earlier), "The mind is back in the earlier body.");
                Assert.That(entMan.HasComponent<WGAdminObserverComponent>(session.AttachedEntity), "The admin is observing.");
                Assert.That(mind.VisitingEntity, Is.EqualTo(session.AttachedEntity), "And can go back to the body, as after aghost.");
            });
        });

        // With none: from an observer that owns the mind.
        await AdminKitFixture.Enter(pair, session, map.GridCoords);
        await server.WaitAssertion(() =>
        {
            Assert.That(returns.EarlierBody(session), Is.Null);
            technician = entMan.SpawnEntity("MobHuman", map.GridCoords);
            minds.ControlMob(session.UserId, technician);
            returns.Grant(technician, null);
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() => returns.Return((technician, entMan.GetComponent<WGReturnToObserverComponent>(technician))));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(minds.TryGetMind(session, out _, out var mind));
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(technician));
                Assert.That(entMan.HasComponent<WGAdminObserverComponent>(session.AttachedEntity), "With no body to go back to, a new observer.");
                Assert.That(mind!.OwnedEntity, Is.EqualTo(session.AttachedEntity));
            });
        });

        await pair.CleanReturnAsync();
    }
}
