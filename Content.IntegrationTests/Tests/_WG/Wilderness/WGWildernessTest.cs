#nullable enable
using System.Numerics;
using Content.Server._Mono.GridClaimer;
using Content.Server._WG.Wilderness;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Wilderness;
using Content.Shared.Mind;
using Content.Shared.Players;
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

    private static void Reset(IConfigurationManager cfg)
    {
        cfg.SetCVar(WildernessCVars.DwellSeconds, WildernessCVars.DwellSeconds.DefaultValue);
        cfg.SetCVar(WildernessCVars.SkullMinutes, WildernessCVars.SkullMinutes.DefaultValue);
        cfg.SetCVar(WildernessCVars.SkullOnFoot, WildernessCVars.SkullOnFoot.DefaultValue);
    }
}
