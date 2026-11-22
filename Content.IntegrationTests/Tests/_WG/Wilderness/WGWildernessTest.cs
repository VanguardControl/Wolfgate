#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.GridClaimer;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.Wilderness;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Wilderness;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Wilderness;

/// <summary>Which zone a player is in, and the Wilderness mark: earned by staying, held while there, run out after.</summary>
[TestFixture]
public sealed class WGWildernessTest
{
    private static EntityUid Grid(IEntityManager em, MapId map, Tile tile, Vector2 at)
    {
        var grid = em.System<SharedMapSystem>().CreateGridEntity(map);
        em.System<SharedMapSystem>().SetTile(grid, grid.Comp, Vector2i.Zero, tile);
        em.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, at);
        return grid.Owner;
    }

    private static void Source(IEntityManager em, EntityUid uid, string zone, float radius)
    {
        var source = em.EnsureComponent<WGZoneSourceComponent>(uid);
        source.Zone = zone;
        source.Radius = radius;
    }

    [Test]
    public async Task AShipThatStaysIsMarkedAndTheMarkRunsOut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var em = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        EntityUid ship = default;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(WildernessCVars.DwellSeconds, 1f);
            cfg.SetCVar(WildernessCVars.SkullMinutes, 0.05f);
            Source(em, map.Grid.Owner, "WGWilderness", 50f);
            ship = Grid(em, map.MapId, map.Tile.Tile, new Vector2(20f, 0f));
            em.EnsureComponent<ShuttleDeedComponent>(ship);
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.TryGetComponent<WGSkullComponent>(ship, out var skull), Is.True, "a ship that stayed in the Wilderness wasn't marked.");
            Assert.That(skull!.ExpiresAt, Is.Null, "the mark should hold while the ship is still in the Wilderness.");
            Assert.That(em.HasComponent<WGSkullComponent>(map.Grid.Owner), Is.False, "the site itself was marked.");
            Assert.That(em.HasComponent<WGWildernessDwellComponent>(ship), Is.False);
            xforms.SetWorldPosition(ship, new Vector2(2000f, 0f));
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.TryGetComponent<WGSkullComponent>(ship, out var skull), Is.True, "the mark ended as soon as the ship left.");
            Assert.That(skull!.ExpiresAt, Is.Not.Null, "the mark's countdown didn't start on leaving.");
        });
        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WGSkullComponent>(ship), Is.False, "the mark didn't run out.");
            Assert.That(em.TryGetComponent<WGZoneRingsComponent>(map.MapUid, out var rings), Is.True, "the map carries no zone edges for radars.");
            Assert.That(rings!.Rings, Has.Count.EqualTo(1));
            Assert.That(rings.Rings[0].Zone.Id, Is.EqualTo("WGWilderness"));
            Assert.That(rings.Rings[0].Radius, Is.GreaterThan(50f));
            Reset(cfg);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ASafeZoneOutranksAWildernessAndAClaimedWreckIsNone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var em = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        EntityUid site = default, ship = default;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(WildernessCVars.DwellSeconds, 60f);
            Source(em, map.Grid.Owner, "WGSafeZone", 100f);
            site = Grid(em, map.MapId, map.Tile.Tile, new Vector2(60f, 0f));
            Source(em, site, "WGWilderness", 200f);
            ship = Grid(em, map.MapId, map.Tile.Tile, new Vector2(30f, 0f));
            em.EnsureComponent<ShuttleDeedComponent>(ship);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WGWildernessDwellComponent>(ship), Is.False, "a ship in both the Safe Zone and a Wilderness was counted as in the Wilderness.");
            xforms.SetWorldPosition(ship, new Vector2(200f, 0f));
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WGWildernessDwellComponent>(ship), Is.True, "a ship in the Wilderness alone isn't being marked.");
            // A claimed wreck is someone's ship: the Wilderness round it ends.
            em.GetComponent<WGZoneSourceComponent>(site).EndsWhenClaimed = true;
            em.EnsureComponent<ClaimableGridComponent>(site).ClaimedBy.Add(ship);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WGWildernessDwellComponent>(ship), Is.False, "a claimed wreck still counted as a Wilderness.");
            Reset(cfg);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task APlayerIsToldWhichZoneTheyAreIn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var em = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(WildernessCVars.DwellSeconds, 1f);
            cfg.SetCVar(WildernessCVars.SkullOnFoot, true);
            Source(em, map.Grid.Owner, "WGWilderness", 50f);

            var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
            var minds = server.System<SharedMindSystem>();
            minds.WipeMind(session.ContentData()?.Mind);
            mob = em.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, mob);
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.TryGetComponent<WGInZoneComponent>(mob, out var inZone), Is.True, "a player on a Wilderness site wasn't told they're in one.");
            Assert.That(inZone!.Zone.Id, Is.EqualTo("WGWilderness"));
            Assert.That(em.HasComponent<WGSkullComponent>(mob), Is.True, "a person on foot in a Wilderness wasn't marked.");
            xforms.SetMapCoordinates(mob, new MapCoordinates(new Vector2(3000f, 0f), map.MapId));
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WGInZoneComponent>(mob), Is.False, "a player far from every zone is still shown one.");
            Assert.That(em.TryGetComponent<WGSkullComponent>(mob, out var skull) && skull.ExpiresAt != null, Is.True,
                "the mark should run down after leaving, not vanish.");
            Reset(cfg);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AMarkHoldsOnAPersonWhosePlayerIsAway()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var em = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var map = await pair.CreateTestMap();
        EntityUid marked = default, waiting = default;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(WildernessCVars.DwellSeconds, 1f);
            cfg.SetCVar(WildernessCVars.SkullOnFoot, true);
            Source(em, map.Grid.Owner, "WGWilderness", 50f);

            // Bodies with no player attached, as a disconnect leaves them.
            marked = em.SpawnEntity("MobHuman", map.GridCoords);
            server.System<WGZoneSystem>().Mark(marked);
            waiting = em.SpawnEntity("MobHuman", map.GridCoords);
            em.EnsureComponent<WGWildernessDwellComponent>(waiting).Since = timing.CurTime;
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.TryGetComponent<WGSkullComponent>(marked, out var skull), Is.True, "a disconnect cleared the mark.");
            Assert.That(skull!.ExpiresAt, Is.Null, "the mark ran down on a person still in the Wilderness because no player was attached.");
            Assert.That(em.HasComponent<WGSkullComponent>(waiting), Is.True, "a disconnect dropped the time counted towards the mark.");
            Reset(cfg);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task APilotAndTheShipTheyFlyShareAMark()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var em = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var map = await pair.CreateTestMap();
        var zones = server.System<WGZoneSystem>();
        EntityUid markedPilot = default, freshShip = default, markedShip = default, freshPilot = default, passenger = default;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(WildernessCVars.DwellSeconds, 60f);

            // A marked pilot takes the helm of an unmarked ship.
            freshShip = Ship(em, map, new Vector2(2000f, 0f));
            markedPilot = Crewman(em, freshShip);
            Helm(em, freshShip, markedPilot);
            zones.Mark(markedPilot);

            // An unmarked pilot takes the helm of a marked ship, with someone else aboard.
            markedShip = Ship(em, map, new Vector2(4000f, 0f));
            freshPilot = Crewman(em, markedShip);
            Helm(em, markedShip, freshPilot);
            passenger = Crewman(em, markedShip);
            zones.Mark(markedShip);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            var pilotMark = em.GetComponent<WGSkullComponent>(markedPilot);
            Assert.That(em.TryGetComponent<WGSkullComponent>(freshShip, out var shipMark), Is.True, "a marked pilot didn't mark the ship they fly.");
            Assert.That(shipMark!.ExpiresAt, Is.Not.Null.And.EqualTo(pilotMark.ExpiresAt), "the ship didn't take the pilot's time left.");

            var markedShipMark = em.GetComponent<WGSkullComponent>(markedShip);
            Assert.That(em.TryGetComponent<WGSkullComponent>(freshPilot, out var inherited), Is.True, "a pilot taking the helm of a marked ship wasn't marked.");
            Assert.That(inherited!.ExpiresAt, Is.Not.Null.And.EqualTo(markedShipMark.ExpiresAt), "the pilot didn't take the ship's time left.");
            Assert.That(em.HasComponent<WGSkullComponent>(passenger), Is.False, "a ship's mark spread to someone aboard who isn't flying it.");
            Reset(cfg);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ACrewmanIsMarkedApartFromTheirShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var em = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var map = await pair.CreateTestMap();
        EntityUid ship = default, mob = default;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(WildernessCVars.DwellSeconds, 1f);
            cfg.SetCVar(WildernessCVars.SkullOnFoot, true);
            Source(em, map.Grid.Owner, "WGWilderness", 50f);
            ship = Ship(em, map, new Vector2(20f, 0f));
            mob = Player(pair, em, new EntityCoordinates(ship, 0.5f, 0.5f));
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.TryGetComponent<WGInZoneComponent>(mob, out var inZone) && inZone.Zone.Id == "WGWilderness", Is.True,
                "a player aboard a ship in a Wilderness wasn't told they're in one.");
            Assert.That(em.TryGetComponent<WGSkullComponent>(ship, out var skull) && skull.ExpiresAt == null, Is.True, "a ship that stayed in the Wilderness wasn't marked.");
            Assert.That(em.HasComponent<WGSkullComponent>(mob), Is.False, "a crewman aboard was marked for the ship's stay without flying it.");
            Assert.That(em.HasComponent<WGWildernessDwellComponent>(mob), Is.False, "a crewman aboard a ship is counting towards a mark of their own.");
            Helm(em, ship, mob);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.TryGetComponent<WGSkullComponent>(mob, out var skull), Is.True, "taking the helm of a marked ship didn't mark the pilot.");
            Assert.That(skull!.ExpiresAt, Is.Null, "a mark taken in the Wilderness should hold there, as the ship's does.");
            Reset(cfg);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AttackingIntoAWildernessFromOutsideMarksTheGunnerAndTheirShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var em = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var map = await pair.CreateTestMap();
        var zones = server.System<WGZoneSystem>();
        EntityUid target = default, inside = default, attacker = default, far = default, farAttacker = default, mob = default, gun = default, fireServer = default;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(WildernessCVars.DwellSeconds, 60f);
            Source(em, map.Grid.Owner, "WGWilderness", 50f);
            target = Ship(em, map, new Vector2(20f, 0f));
            inside = Ship(em, map, new Vector2(30f, 0f));
            attacker = Ship(em, map, new Vector2(2000f, 0f));
            far = Ship(em, map, new Vector2(3000f, 0f));
            farAttacker = Ship(em, map, new Vector2(3500f, 0f));
            mob = Player(pair, em, new EntityCoordinates(attacker, 0.5f, 0.5f));
            gun = em.SpawnEntity(null, new EntityCoordinates(attacker, 0.5f, 0.5f));
            fireServer = em.SpawnEntity(null, new EntityCoordinates(attacker, 0.5f, 0.5f));
            em.EnsureComponent<FireControllableComponent>(gun).ControllingServer = fireServer;
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            zones.NoteAim(mob, fireServer, new List<NetEntity> { em.GetNetEntity(gun) });
            Hit(em, target, inside);
            Hit(em, far, farAttacker);
            Assert.That(em.HasComponent<WGSkullComponent>(inside), Is.False, "fighting inside the Wilderness marked at once, without the stay.");
            Assert.That(em.HasComponent<WGSkullComponent>(farAttacker), Is.False, "a fight outside every Wilderness marked the attacker.");

            Hit(em, target, attacker);
            Assert.That(em.TryGetComponent<WGSkullComponent>(attacker, out var shipMark), Is.True, "a ship attacking into a Wilderness from outside wasn't marked.");
            Assert.That(shipMark!.ExpiresAt, Is.Not.Null, "the attacker's mark should run down, as it is outside the Wilderness.");
            Assert.That(em.HasComponent<WGSkullComponent>(mob), Is.False, "a gunner whose guns never fired was marked for the ship's hit.");

            Shot(em, gun);
            Hit(em, target, attacker);
            Assert.That(em.TryGetComponent<WGSkullComponent>(mob, out var gunnerMark), Is.True, "the gunner attacking into a Wilderness wasn't marked.");
            Assert.That(gunnerMark!.ExpiresAt, Is.Not.Null);
            Assert.That(em.HasComponent<WGSkullComponent>(target), Is.False, "the ship attacked was marked for it.");
            Reset(cfg);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnAttackIsJudgedByWhereTheWildernessIsNow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var em = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        EntityUid target = default, attacker = default;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(WildernessCVars.DwellSeconds, 60f);
            Source(em, map.Grid.Owner, "WGWilderness", 50f);
            target = Ship(em, map, new Vector2(20f, 0f));
            attacker = Ship(em, map, new Vector2(2000f, 0f));
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            // The site moves off before the next once-a-second count of the zones.
            xforms.SetWorldPosition(map.Grid.Owner, new Vector2(-5000f, 0f));
            Hit(em, target, attacker);
            Assert.That(em.HasComponent<WGSkullComponent>(attacker), Is.False, "an attack was judged by where a Wilderness used to be.");
            Reset(cfg);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StaffMarkAndClearAPersonAndAShipAndItsPilot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var em = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var map = await pair.CreateTestMap();
        var zones = server.System<WGZoneSystem>();
        EntityUid ship = default, pilot = default, walker = default;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(WildernessCVars.DwellSeconds, 60f);
            ship = Ship(em, map, new Vector2(2000f, 0f));
            pilot = Crewman(em, ship);
            Helm(em, ship, pilot);
            walker = em.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(4000f, 0f), map.MapId));

            Assert.That(zones.CanCarry(walker), Is.True);
            Assert.That(zones.CanCarry(map.Grid.Owner), Is.False, "a station grid counted as a person who can carry a mark.");
            Assert.That(zones.IsShip(map.Grid.Owner), Is.False, "a station counted as a ship.");

            zones.StaffMark(walker);
            zones.StaffMark(ship);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.TryGetComponent<WGSkullComponent>(walker, out var skull) && skull.ExpiresAt != null, Is.True,
                "a staff mark outside a Wilderness should run down.");
            Assert.That(em.HasComponent<WGSkullComponent>(pilot), Is.True, "the pilot didn't share the ship's staff mark.");

            var cleared = zones.StaffClear(ship);
            Assert.That(cleared, Does.Contain(pilot), "clearing a ship left its pilot's mark, which would mark it again.");
            zones.StaffClear(walker);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WGSkullComponent>(ship), Is.False, "the ship's mark came back after staff cleared it.");
            Assert.That(em.HasComponent<WGSkullComponent>(pilot), Is.False, "the pilot's mark came back after staff cleared the ship.");
            Assert.That(em.HasComponent<WGSkullComponent>(walker), Is.False);
            Reset(cfg);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A bought ship of one tile.</summary>
    private static EntityUid Ship(IEntityManager em, TestMapData map, Vector2 at)
    {
        var ship = Grid(em, map.MapId, map.Tile.Tile, at);
        em.EnsureComponent<ShuttleDeedComponent>(ship);
        return ship;
    }

    /// <summary>Someone aboard a ship, kept conscious in its vacuum.</summary>
    private static EntityUid Crewman(IEntityManager em, EntityUid ship)
    {
        var mob = em.SpawnEntity("MobHuman", new EntityCoordinates(ship, 0.5f, 0.5f));
        em.RemoveComponent<BarotraumaComponent>(mob);
        em.RemoveComponent<RespiratorComponent>(mob);
        return mob;
    }

    /// <summary>Puts someone at a new helm on a ship.</summary>
    private static void Helm(IEntityManager em, EntityUid ship, EntityUid pilot)
    {
        var console = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(ship, 0.5f, 0.5f));
        em.EnsureComponent<PilotComponent>(pilot);
        em.System<ShuttleConsoleSystem>().AddPilot(console, pilot, em.GetComponent<ShuttleConsoleComponent>(console));
    }

    /// <summary>The test client's player, in a new body.</summary>
    private static EntityUid Player(TestPair pair, IEntityManager em, EntityCoordinates at)
    {
        var server = pair.Server;
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var minds = server.System<SharedMindSystem>();
        minds.WipeMind(session.ContentData()?.Mind);
        var mob = em.SpawnEntity("MobHuman", at);
        em.RemoveComponent<BarotraumaComponent>(mob);
        em.RemoveComponent<RespiratorComponent>(mob);
        minds.TransferTo(minds.CreateMind(session.UserId).Owner, mob);
        return mob;
    }

    /// <summary>Reports a ship weapon hit on one ship from another, as the ship guns do.</summary>
    private static void Hit(IEntityManager em, EntityUid target, EntityUid attacker)
    {
        var ev = new WGCrewHullHitEvent(target, attacker);
        em.EventBus.RaiseEvent(EventSource.Local, ref ev);
    }

    /// <summary>A ship gun taking a shot, as its rounds leave it.</summary>
    private static void Shot(IEntityManager em, EntityUid gun)
    {
        var ev = new GunShotEvent(gun, new List<(EntityUid? Uid, IShootable Shootable)>(), new EntityCoordinates(gun, Vector2.Zero));
        em.EventBus.RaiseLocalEvent(gun, ref ev);
    }

    private static void Reset(IConfigurationManager cfg)
    {
        cfg.SetCVar(WildernessCVars.DwellSeconds, WildernessCVars.DwellSeconds.DefaultValue);
        cfg.SetCVar(WildernessCVars.SkullMinutes, WildernessCVars.SkullMinutes.DefaultValue);
        cfg.SetCVar(WildernessCVars.SkullOnFoot, WildernessCVars.SkullOnFoot.DefaultValue);
    }
}
