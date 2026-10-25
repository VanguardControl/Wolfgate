#nullable enable
using System.Numerics;
using Content.Client._WF.Roofs;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Roofs;

/// <summary>
/// A roof covers only a building's open floor, never its walls, windows or doors; a shut airlock bounds the room that
/// lifts; and a ghost or observer sees no roofs at all.
/// </summary>
[TestFixture]
[TestOf(typeof(WFRoofSystem))]
public sealed class WFRoofCoverageTest
{
    // One roofed row: 0 floor, 1 shut airlock, 2 floor, 3 wall, 4 window, 5 table, 6 floor.
    private static readonly (int X, string? Proto)[] Row =
    {
        (0, null), (1, "Airlock"), (2, null), (3, "WallSolid"), (4, "Window"), (5, "Table"), (6, null),
    };

    [Test]
    public async Task RoofsCoverOnlyOpenFloorAndNeverForGhosts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var sEnt = server.EntMan;

        EntityUid wall = default;
        EntityUid human = default;
        EntityUid outsider = default;
        await server.WaitPost(() =>
        {
            var maps = sEnt.System<SharedMapSystem>();
            var roofs = sEnt.System<SharedRoofSystem>();
            var xforms = sEnt.System<SharedTransformSystem>();
            var grid = map.Grid;
            sEnt.RemoveComponent<ImplicitRoofComponent>(grid);
            sEnt.EnsureComponent<RoofComponent>(grid);
            foreach (var (x, proto) in Row)
            {
                var index = new Vector2i(x, 0);
                maps.SetTile(grid, grid.Comp, index, map.Tile.Tile);
                roofs.SetRoof((grid, grid.Comp, null), index, true);
                if (proto == null)
                    continue;
                var uid = sEnt.SpawnEntity(proto, maps.GridTileToLocal(grid, grid.Comp, index));
                if (!sEnt.GetComponent<TransformComponent>(uid).Anchored)
                    xforms.AnchorEntity(uid);
                if (proto == "WallSolid")
                    wall = uid;
            }

            human = sEnt.SpawnEntity("MobHuman", maps.GridTileToLocal(grid, grid.Comp, new Vector2i(0, 0)));
            outsider = sEnt.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(3f, 6f), map.MapId));
            server.PlayerMan.SetAttachedEntity(pair.Player!, outsider);
        });
        await pair.RunTicksSync(15);

        var cEnt = client.EntMan;
        var cRoofs = cEnt.System<WFRoofSystem>();
        Entity<MapGridComponent> cGrid = default;
        await client.WaitPost(() =>
        {
            var uid = cEnt.GetEntity(sEnt.GetNetEntity(map.Grid.Owner));
            cGrid = (uid, cEnt.GetComponent<MapGridComponent>(uid));
        });

        bool Covers(int x) => cRoofs.Covers(cGrid, new Vector2i(x, 0));

        await client.WaitAssertion(() =>
        {
            Assert.That(cRoofs.Enabled, "Someone with a body sees roofs.");
            Assert.That(Covers(0) && Covers(2) && Covers(6), "Open floor under the roof is covered.");
            Assert.That(Covers(5), "Furniture inside is roofed over with the floor.");
            Assert.That(Covers(1), Is.False, "An airlock shows through the roof.");
            Assert.That(Covers(3), Is.False, "A wall shows through the roof.");
            Assert.That(Covers(4), Is.False, "A window shows through the roof.");
        });

        // Standing inside, the room lifts up to the shut airlock; the room past it stays covered.
        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, human));
        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
        {
            Assert.That(cRoofs.Grid, Is.EqualTo(cGrid.Owner));
            Assert.That(Covers(0), Is.False, "The room the player stands in lifts.");
            Assert.That(Covers(2), Is.True, "A shut airlock keeps the next room covered.");
        });

        // A wall torn down leaves floor to roof over: the remembered structure is forgotten.
        await server.WaitPost(() =>
        {
            sEnt.DeleteEntity(wall);
            server.PlayerMan.SetAttachedEntity(pair.Player!, outsider);
        });
        await pair.RunTicksSync(15);
        await client.WaitAssertion(() => Assert.That(Covers(3), "A removed wall's tile is roofed again."));

        // Ghosts and admin ghosts see no roofs.
        foreach (var observer in new[] { "MobObserver", "AdminObserver" })
        {
            EntityUid ghost = default;
            await server.WaitPost(() =>
            {
                ghost = sEnt.SpawnEntity(observer, new MapCoordinates(new Vector2(3f, 6f), map.MapId));
                server.PlayerMan.SetAttachedEntity(pair.Player!, ghost);
            });
            await pair.RunTicksSync(15);
            await client.WaitAssertion(() =>
            {
                Assert.That(cRoofs.Enabled, Is.False, $"A {observer} sees roofs.");
                Assert.That(Covers(0) || Covers(6), Is.False, $"A roof is drawn for a {observer}.");
            });
            await server.WaitPost(() =>
            {
                server.PlayerMan.SetAttachedEntity(pair.Player!, outsider);
                sEnt.DeleteEntity(ghost);
            });
            await pair.RunTicksSync(5);
        }

        await server.WaitPost(() =>
        {
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            sEnt.DeleteEntity(human);
            sEnt.DeleteEntity(outsider);
        });
        await pair.CleanReturnAsync();
    }
}
