using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Bank;
using Content.Server._WF.Drydock;
using Content.Server._WF.Encounters.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>Tests the Chop-Shop's consoles: sheets and cash for a break, no repairs, and what counts as lawful property.</summary>
[TestFixture]
public sealed class ChopShopTest
{
    private const int HullSize = 4;

    /// <summary>Builds a mobile crane yard worked by a chop-shop console, with a 4x4 steel-walled hull in its berth.</summary>
    private static (EntityUid Console, EntityUid Crane, EntityUid Ship) SetUpChopShop(TestPair pair, MapId map)
    {
        var em = pair.Server.EntMan;
        var yard = MobileDrydockTest.SpawnMobileYard(pair, map);
        var at = em.GetComponent<TransformComponent>(yard.Console).Coordinates;
        em.DeleteEntity(yard.Console);
        var console = em.SpawnEntity("WFComputerChopShop", at);
        DrydockTest.LinkCrane(pair, console, yard.Crane);

        var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
        Assert.That(pair.Server.System<WFDrydockSystem>().TryGetBerth(console, comp, out var berth), "The crane should make a berth.");

        var ship = DrydockTest.CreateShip(pair, map);
        DrydockTest.SetFloor(pair, ship, HullSize);
        DrydockTest.SpawnAnchored(pair, ship, "WallReinforced", new Vector2i(0, 0));
        DrydockTest.SpawnAnchored(pair, ship, "Autolathe", new Vector2i(2, 2));
        DrydockTest.PlaceOver(pair, ship, yard.Grid, berth.Area.Center);
        return (console, yard.Crane, ship);
    }

    /// <summary>
    /// A break at a chop shop stores nothing in its crane: it hands over a share of the yield as sheets by the console
    /// and pays the breaker cash for the rest, as its preview said. It sells no repairs.
    /// </summary>
    [Test]
    public async Task BreakPaysSheetsAndCash()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var bank = server.System<BankSystem>();

        EntityUid console = default, crane = default, ship = default, actor = default;
        var before = 0;
        var preview = 0;
        await server.WaitAssertion(() =>
        {
            (console, crane, ship) = SetUpChopShop(pair, map.MapId);
            actor = DrydockTest.CreatePayer(pair, map.MapId);
            Assert.That(bank.TryGetBalance(actor, out before), "The breaker should have an account.");
        });

        await MobileDrydockTest.WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            drydock.UpdateUi(console, force: true);
            Assert.That(server.System<SharedUserInterfaceSystem>().TryGetUiState<WFDrydockConsoleState>(console,
                WFDrydockConsoleUiKey.Key, out var state), Is.True, "The console should have a state.");
            Assert.Multiple(() =>
            {
                Assert.That(state!.BreakOnly, Is.True, "A chop shop should say it only breaks.");
                Assert.That(state.PaysWithMaterials, Is.False, "A chop shop should list no supplies.");
                Assert.That(state.Upgrades, Is.Empty, "A chop shop should offer no refits.");
                Assert.That(state.BreakCash, Is.GreaterThan(0), "The preview should quote cash.");
                Assert.That(state.BreakWarrant, Is.False, "An unregistered hull is anyone's.");
            });
            preview = state!.BreakCash;

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartRepair(console, comp, ship, actor), Is.False, "A chop shop should refuse a repair.");
            Assert.That(drydock.TryStartBreak(console, comp, ship, actor), Is.True, "The break should start.");
        });

        await DrydockTest.RunUntil(pair, 400, 30, () => !em.HasComponent<WFDrydockJobComponent>(console));

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "The break should end.");
            var storage = server.System<SharedMaterialStorageSystem>();
            var consoleXform = em.GetComponent<TransformComponent>(console);
            var sheets = 0;
            var query = em.EntityQueryEnumerator<MaterialComponent, StackComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var stack, out var xform))
            {
                if (xform.GridUid == consoleXform.GridUid)
                    sheets += stack.Count;
            }

            Assert.That(bank.TryGetBalance(actor, out var after), "The breaker should still have an account.");
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(ship), Is.True, "The hull should be gone.");
                Assert.That(storage.GetMaterialAmount(crane, "Steel", localOnly: true), Is.Zero,
                    "A chop shop should store nothing in its crane.");
                Assert.That(sheets, Is.GreaterThan(0), "A share of the yield should be handed over as sheets.");
                Assert.That(after - before, Is.EqualTo(preview), "The breaker should be paid what the preview quoted.");
            });
            TestContext.Out.WriteLine($"Chop shop paid {after - before} for a {HullSize}x{HullSize} hull and {sheets} sheets.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A deed in someone else's name and a live lawful encounter ship are lawful property; the breaker's own deed, an
    /// outlaw's encounter ship and a derelict are not.
    /// </summary>
    [Test]
    public async Task LawfulPropertyRules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        await server.WaitAssertion(() =>
        {
            var console = em.SpawnEntity("WFComputerChopShop", new MapCoordinates(0, 0, map.MapId));
            var chop = em.GetComponent<WFDrydockChopShopComponent>(console);
            var breaker = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map.MapId));
            server.System<MetaDataSystem>().SetEntityName(breaker, "Jo Breaker");

            var deeded = DrydockTest.CreateShip(pair, map.MapId, new Vector2(100f, 0f));
            var deed = em.AddComponent<ShuttleDeedComponent>(deeded);
#pragma warning disable RA0002
            deed.ShuttleOwner = "Someone Else";
#pragma warning restore RA0002

            var pirate = EncounterShip(pair, map.MapId, "WFEncounterPirateRaiders", "raider", derelict: false);
            var navy = EncounterShip(pair, map.MapId, "WFEncounterTsfPatrol", "lead", derelict: false);
            var wreck = EncounterShip(pair, map.MapId, "WFEncounterTsfPatrol", "lead", derelict: true);

            Assert.Multiple(() =>
            {
                Assert.That(drydock.IsLawfulProperty(chop, deeded, breaker), Is.True, "Someone else's ship is lawful property.");
                Assert.That(drydock.IsLawfulProperty(chop, pirate, breaker), Is.False, "A pirate's ship is anyone's.");
                Assert.That(drydock.IsLawfulProperty(chop, navy, breaker), Is.True, "A navy ship is lawful property.");
                Assert.That(drydock.IsLawfulProperty(chop, wreck, breaker), Is.False, "A derelict is anyone's.");
            });

#pragma warning disable RA0002
            deed.ShuttleOwner = "Jo Breaker";
#pragma warning restore RA0002
            Assert.That(drydock.IsLawfulProperty(chop, deeded, breaker), Is.False, "The breaker's own ship is theirs to break.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Makes a grid one ship of a stand-in encounter of the given prototype.</summary>
    private static EntityUid EncounterShip(TestPair pair, MapId map, string prototype, string key, bool derelict)
    {
        var em = pair.Server.EntMan;
        var ship = DrydockTest.CreateShip(pair, map, new Vector2(-100f, 0f));
        var encounter = em.SpawnEntity(null, new MapCoordinates(0, 0, map));
        var comp = em.AddComponent<WFEncounterComponent>(encounter);
        comp.Prototype = prototype;
        comp.Ships[key] = new WFEncounterShipState { Grid = ship, Derelict = derelict };
        var member = em.AddComponent<WFEncounterGridComponent>(ship);
        member.Encounter = encounter;
        member.Key = key;
        return ship;
    }

    /// <summary>The Chop-Shop map loads with four chop-shop berths, each with its cranes, and a fence who books nothing.</summary>
    [Test]
    public async Task ChopShopMapLoads()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(map.MapId, new ResPath("/Maps/_WF/Drydock/chopshop.yml"),
                out var grid, offset: new Vector2(500f, 500f)), "chopshop.yml should load.");
            var station = grid!.Value.Owner;

            var consoles = 0;
            var query = em.EntityQueryEnumerator<WFDrydockConsoleComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var comp, out var xform))
            {
                if (xform.GridUid != station)
                    continue;

                consoles++;
                Assert.That(em.HasComponent<WFDrydockChopShopComponent>(uid), $"The {comp.Berth} console should be a chop shop.");
                Assert.That(drydock.TryGetBerth(uid, comp, out _), $"The {comp.Berth} berth should have cranes.");
            }

            Assert.Multiple(() =>
            {
                Assert.That(consoles, Is.EqualTo(4), "The Chop-Shop should have four berths.");
                Assert.That(DrydockTest.HailContactsOn(pair, station), Has.Count.EqualTo(1), "The fence should answer hails.");
                Assert.That(em.EntityQuery<WFDrydockOperatorComponent>().Any(), Is.False, "Nobody at the Chop-Shop books berths.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
