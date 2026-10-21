#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Strangers;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.NPC.Components;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Strangers;

/// <summary>Strangers walk up and hail, sell, get mad when refused, and the stalker turns up aboard your ship.</summary>
[TestFixture]
[TestOf(typeof(WFStrangerSystem))]
public sealed class StrangerTest
{
    private static async Task<(TestMapData Map, EntityUid Player)> Rig(TestPair pair, int size = 12)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid player = default;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -size; x <= size; x++)
            for (var y = -size; y <= size; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            // Ground, as on a planet: without gravity everyone drifts on after stopping.
            var gravity = server.EntMan.EnsureComponent<Content.Shared.Gravity.GravityComponent>(map.Grid.Owner);
            gravity.Enabled = true;
            gravity.Inherent = true;
            server.EntMan.Dirty(map.Grid.Owner, gravity);
            player = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, player);
        });
        return (map, player);
    }

    [Test]
    public async Task TheDealerHailsAndTurnsOnYouWhenRefused()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair);
        EntityUid dealer = default;
        await server.WaitPost(() => dealer = entMan.SpawnEntity("WFStrangerDealer", new EntityCoordinates(map.Grid.Owner, 6.5f, 0.5f)));
        await pair.RunSeconds(8);

        var strangers = server.System<WFStrangerSystem>();
        await server.WaitAssertion(() =>
        {
            var stranger = entMan.GetComponent<WFStrangerComponent>(dealer);
            Assert.That(stranger.Target, Is.EqualTo(player), "The dealer never went for the player.");
            Assert.That(stranger.Hailed, Is.True, "The dealer walked up but never hailed.");

            strangers.StartConversation((dealer, stranger), player);
            Assert.That(stranger.Node, Is.EqualTo("pitch"));
            // "Not interested."
            strangers.Choose((dealer, stranger), player, 1);
            Assert.That(entMan.GetComponent<FactionExceptionComponent>(dealer).Hostiles, Does.Contain(player), "Refused, he should go for you.");
            Assert.That(entMan.GetComponent<HTNComponent>(dealer).Enabled, Is.True, "His own brain should take over to fight.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheDealerSellsForCashInHand()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair);
        await server.WaitAssertion(() =>
        {
            var hands = server.System<SharedHandsSystem>();
            var cash = entMan.SpawnEntity("SpaceCash500", entMan.GetComponent<TransformComponent>(player).Coordinates);
            Assert.That(hands.TryPickupAnyHand(player, cash), Is.True);
            var dealer = entMan.SpawnEntity("WFStrangerDealer", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            var stranger = entMan.GetComponent<WFStrangerComponent>(dealer);
            var strangers = server.System<WFStrangerSystem>();

            strangers.StartConversation((dealer, stranger), player);
            strangers.Choose((dealer, stranger), player, 0);
            Assert.That(stranger.Node, Is.EqualTo("menu"));
            strangers.Choose((dealer, stranger), player, 0);

            Assert.That(entMan.GetComponent<StackComponent>(cash).Count, Is.EqualTo(300), "Two hundred should come out of the cash in hand.");
            var held = hands.EnumerateHeld(player).Select(item => entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID).ToList();
            Assert.That(held.Any(id => id is "CigaretteSpaceDrugs" or "JointRainbow" or "BluntRainbow"), Is.True, "No drugs were handed over.");
            var angry = entMan.TryGetComponent<FactionExceptionComponent>(dealer, out var exceptions) ? exceptions.Hostiles : new HashSet<EntityUid>();
            Assert.That(angry, Does.Not.Contain(player), "A sale shouldn't anger him.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShotWithAGunHeTurnsAndRemembers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair);
        await server.WaitAssertion(() =>
        {
            var gun = entMan.SpawnEntity("WeaponPistolMk58", entMan.GetComponent<TransformComponent>(player).Coordinates);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(player, gun), Is.True);
            var nut = entMan.SpawnEntity("WFStrangerConspiracist", new EntityCoordinates(map.Grid.Owner, 3.5f, 0.5f));
            var stranger = entMan.GetComponent<WFStrangerComponent>(nut);
            var damage = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Piercing"), 5);
            server.System<DamageableSystem>().TryChangeDamage(nut, damage, origin: gun);

            Assert.That(entMan.GetComponent<FactionExceptionComponent>(nut).Hostiles, Does.Contain(player), "Shot, he should go for the shooter.");
            Assert.That(stranger.Hostile, Is.True);
            Assert.That(entMan.GetComponent<HTNComponent>(nut).Enabled, Is.True);

            var strangers = server.System<WFStrangerSystem>();
            strangers.StartConversation((nut, stranger), player);
            Assert.That(stranger.Node, Is.Null, "In his bad books, there's no talking to him.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheCampWarnsYouOffThenAttacks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair, 20);
        EntityUid first = default, second = default;
        await server.WaitPost(() =>
        {
            var xforms = server.System<SharedTransformSystem>();
            xforms.SetCoordinates(player, new EntityCoordinates(map.Grid.Owner, 13.5f, 0.5f));
            entMan.SpawnEntity("WFStrangerCampHome", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            first = entMan.SpawnEntity("WFStrangerMugger", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            second = entMan.SpawnEntity("WFStrangerBeggar", new EntityCoordinates(map.Grid.Owner, -1.5f, 0.5f));
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var stranger = entMan.GetComponent<WFStrangerComponent>(first);
            Assert.That(stranger.Home, Is.Not.Null, "Spawned in the camp, it should be his home.");
            Assert.That(stranger.Warned, Does.ContainKey(player), "He should warn off someone coming up to the camp.");
            Assert.That(stranger.Hostile, Is.False, "A warning first, not a fight.");
            Assert.That(stranger.Target, Is.Null, "Someone with a home doesn't go after strangers to talk.");
        });

        await server.WaitPost(() =>
            server.System<SharedTransformSystem>().SetCoordinates(player, new EntityCoordinates(map.Grid.Owner, 5.5f, 0.5f)));
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFStrangerComponent>(first).Hostile, Is.True, "Inside the camp, he should attack.");
            Assert.That(entMan.GetComponent<WFStrangerComponent>(second).Hostile, Is.True, "...and the rest of the camp with him.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ARoamerWandersOff()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair, 30);
        EntityUid roamer = default;
        await server.WaitPost(() =>
        {
            // Out of his sight, close enough to keep him awake.
            server.System<SharedTransformSystem>().SetCoordinates(player, new EntityCoordinates(map.Grid.Owner, -25.5f, 0.5f));
            roamer = entMan.SpawnEntity("WFStrangerBeggar", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
        });
        await pair.RunSeconds(60);
        await server.WaitAssertion(() =>
        {
            var position = entMan.GetComponent<TransformComponent>(roamer).LocalPosition;
            Assert.That(Vector2.Distance(position, new Vector2(0.5f, 0.5f)), Is.GreaterThan(2f), "With nobody about, he should wander.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheStalkerTurnsUpAboardYourShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair);
        EntityUid stalker = default, ship = default;
        await server.WaitPost(() =>
        {
            stalker = entMan.SpawnEntity("WFStrangerStalker", new EntityCoordinates(map.Grid.Owner, 7.5f, 0.5f));
#pragma warning disable RA0002 // The test shortens his patience.
            entMan.GetComponent<WFStrangerComponent>(stalker).BoardDelay = TimeSpan.FromSeconds(2);
#pragma warning restore RA0002
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFStrangerComponent>(stalker).Target, Is.EqualTo(player), "The stalker never picked his mark."));

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var grid = maps.CreateGridEntity(map.MapId);
            ship = grid.Owner;
            entMan.GetComponent<TransformComponent>(ship).LocalPosition = new Vector2(40, 0);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = 0; x < 8; x++)
            for (var y = 0; y < 4; y++)
                maps.SetTile(ship, grid.Comp, new Vector2i(x, y), new Tile(plating));
            entMan.EnsureComponent<ShuttleComponent>(ship);
            var xforms = server.System<SharedTransformSystem>();
            xforms.SetCoordinates(player, new EntityCoordinates(ship, 0.5f, 0.5f));
        });
        await pair.RunSeconds(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(stalker).GridUid, Is.EqualTo(ship), "The stalker should turn up aboard.");
            var xforms = server.System<SharedTransformSystem>();
            Assert.That(Vector2.Distance(xforms.GetWorldPosition(stalker), xforms.GetWorldPosition(player)), Is.GreaterThan(3f),
                "...somewhere out of the way, not on top of his mark.");
        });
        await pair.CleanReturnAsync();
    }
}
