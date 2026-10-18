#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.PlanetPois;
using Robust.Server.Physics;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>A turned site's tiles, footprint and entrances land where the grid merge puts its tiles.</summary>
[TestFixture]
public sealed class PlanetPoiRotationTest
{
    private const string Station = "/Maps/_WF/PlanetPois/verda_gas_station.yml";
    private const string House = "/Maps/_WF/PlanetPois/verda_house_1.yml";

    [TestCase(Station, 0)]
    [TestCase(Station, 1)]
    [TestCase(Station, 2)]
    [TestCase(Station, 3)]
    [TestCase(House, 0)]
    [TestCase(House, 1)]
    public async Task ARoadsideSiteTurnsWithItsRoad(string site, int turns)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var entry = server.ProtoMan.Index<WFPlanetPoiTablePrototype>("WFPoisAsclepiu").Entries.Single(e => e.MapPath == site);
            Assert.That(entry.Rotate, Is.True);
            var footprint = entry.Footprint!.Value;
            var turnedBox = WFPlanetPoiSystem.TurnBox(footprint, turns);

            var maps = server.System<SharedMapSystem>();
            maps.CreateMap(out var sourceMap, runMapInit: false);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(sourceMap, new ResPath(site), out var loaded), Is.True);
            var source = loaded!.Value;
            var sourceTiles = maps.GetAllTiles(source.Owner, source.Comp).ToDictionary(t => t.GridIndices, t => t.Tile.TypeId);

            var target = maps.CreateMap(out _, runMapInit: false);
            var targetGrid = entMan.AddComponent<MapGridComponent>(target);
            server.System<SharedTransformSystem>().SetCoordinates(source.Owner, new EntityCoordinates(target, System.Numerics.Vector2.Zero));
            server.System<GridFixtureSystem>().Merge(target, source.Owner, Vector2i.Zero, Angle.FromDegrees(90 * turns), targetGrid, source.Comp);
            var merged = new List<EntityUid>();
            var children = entMan.GetComponent<TransformComponent>(target).ChildEnumerator;
            while (children.MoveNext(out var child))
                merged.Add(child);
            server.System<WFPlanetPoiSystem>().UnturnFixedRotation(merged);

            foreach (var (tile, type) in sourceTiles)
            {
                var turned = WFPlanetPoiSystem.TurnTile(tile, turns);
                Assert.That(maps.GetTileRef(target, targetGrid, turned).Tile.TypeId, Is.EqualTo(type), $"{tile} should land on {turned}.");
                Assert.That(Inside(turnedBox, turned), Is.True, $"{turned} is outside the turned footprint {turnedBox}.");
            }

            foreach (var socket in entry.Sockets)
            {
                var offset = WFPlanetPoiSystem.TurnTile(socket.Offset, turns);
                var direction = WFPlanetPoiSystem.TurnDirection(socket.Direction, turns);
                var step = direction switch
                {
                    WFPlanetPoiDirection.North => new Vector2i(0, 1),
                    WFPlanetPoiDirection.East => new Vector2i(1, 0),
                    WFPlanetPoiDirection.South => new Vector2i(0, -1),
                    _ => new Vector2i(-1, 0),
                };
                Assert.That(Inside(turnedBox, offset) && !Inside(turnedBox, offset + step), Is.True,
                    $"The entrance at {socket.Offset} should still face out of the site after {turns} turns.");
                Assert.That(maps.GetTileRef(target, targetGrid, offset).Tile.IsEmpty, Is.False, "An entrance must stand on the site's road.");
            }

            entMan.DeleteEntity(target);
            maps.DeleteMap(sourceMap);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The site code's containment: the right and top edges are outside.</summary>
    private static bool Inside(Box2i box, Vector2i tile) =>
        tile.X >= box.Left && tile.X < box.Right && tile.Y >= box.Bottom && tile.Y < box.Top;
}
