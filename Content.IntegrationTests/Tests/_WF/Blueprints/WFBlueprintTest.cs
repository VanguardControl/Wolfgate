#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Blueprints;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.Blueprints;
using Content.Shared.Maps;
using Content.Shared.Research.Components;
using Content.Shared.Salvage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Blueprints;

/// <summary>
/// A locator hunt claims a site of its tier and hides a shard per pattern there; assembling the shards ends the hunt
/// and frees the site, and a Construct unlocks its pattern's recipes on the locator's server.
/// </summary>
[TestFixture]
[TestOf(typeof(WFBlueprintSystem))]
public sealed class WFBlueprintTest
{
    [Test]
    public async Task HuntPlacesShardsAndConstructUnlocks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var maps = server.System<SharedMapSystem>();
        var blueprints = server.System<WFBlueprintSystem>();

        var groundMap = EntityUid.Invalid;
        var shipMap = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var floor = new Tile(tileDefs["FloorSteel"].TileId);

            // A ground with one Moderate site and one Extreme site, standing in for a world's POI record.
            groundMap = maps.CreateMap(out var groundMapId);
            var ground = maps.CreateGridEntity(groundMapId);
            var record = entMan.AddComponent<WFPlanetPoiComponent>(ground.Owner);
            record.NetworkReady = true;
            foreach (var (left, rating) in new[] { (0, DifficultyRating.Moderate), (20, DifficultyRating.Extreme) })
            {
                var site = new WFPlanetPoiSite
                {
                    Name = "wf-blueprint-locator-title",
                    Bounds = new Box2i(left, 0, left + 6, 6),
                    Difficulty = rating,
                    Signal = EntityUid.Invalid,
                };

                for (var x = left; x < left + 6; x++)
                for (var y = 0; y < 6; y++)
                {
                    maps.SetTile(ground.Owner, ground.Comp, new Vector2i(x, y), floor);
                    site.Tiles.Add(new Vector2i(x, y));
                }

                record.Sites.Add(site);
            }

            // A ship with a research server and a locator, which links to it at map init.
            shipMap = maps.CreateMap(out var shipMapId);
            var ship = maps.CreateGridEntity(shipMapId);
            maps.SetTile(ship.Owner, ship.Comp, Vector2i.Zero, floor);
            maps.SetTile(ship.Owner, ship.Comp, new Vector2i(1, 0), floor);
            var researchServer = entMan.SpawnEntity("ResearchAndDevelopmentServer", new EntityCoordinates(ship.Owner, 0.5f, 0.5f));
            var locatorUid = entMan.SpawnEntity("WFBlueprintLocator", new EntityCoordinates(ship.Owner, 1.5f, 0.5f));
            var locator = new Entity<WFBlueprintLocatorComponent>(locatorUid, entMan.GetComponent<WFBlueprintLocatorComponent>(locatorUid));
            locator.Comp.Fees = new[] { 0, 0, 0 };

            // Patterns of two tiers in one request are refused.
            Assert.That(blueprints.TryLocate(locator, locatorUid, new List<string> { "WFPatternOmnitool", "WFPatternHandTeleporter" }, out _), Is.False);

            Assert.That(blueprints.TryLocate(locator, locatorUid, new List<string> { "WFPatternOmnitool", "WFPatternJetpackBlack" }, out var error), Is.True, error);
            var hunt = locator.Comp.Hunt;
            Assert.That(hunt, Is.Not.Null);
            Assert.That(hunt!.SiteIndex, Is.EqualTo(0), "A tier I hunt took a site of the wrong rating.");
            Assert.That(record.Sites[0].BlueprintClaim, Is.EqualTo(locatorUid));
            Assert.That(hunt.Shards, Has.Count.EqualTo(2));

            foreach (var shard in hunt.Shards)
            {
                var tile = maps.LocalToTile(ground.Owner, ground.Comp, entMan.GetComponent<TransformComponent>(shard).Coordinates);
                Assert.That(record.Sites[0].Bounds.Contains(tile), Is.True, "A shard lies outside its site.");
            }

            // A second hunt waits for the first.
            Assert.That(blueprints.TryLocate(locator, locatorUid, new List<string> { "WFPatternHandTeleporter" }, out _), Is.False);

            // Assembling: each shard becomes a Construct; the last one ends the hunt and frees the site.
            var constructs = new List<EntityUid>();
            foreach (var shard in hunt.Shards.ToList())
            {
                var pattern = proto.Index<WFBlueprintPatternPrototype>(entMan.GetComponent<WFBlueprintShardComponent>(shard).Pattern!.Value);
                constructs.Add(blueprints.SpawnFor("WFBlueprintConstruct", pattern, new EntityCoordinates(ship.Owner, 0.5f, 0.5f), "wf-blueprint-construct-name"));
                entMan.DeleteEntity(shard);
            }

            Assert.That(locator.Comp.Hunt, Is.Null, "The hunt outlived its shards.");
            Assert.That(record.Sites[0].BlueprintClaim, Is.Null, "The site stayed claimed.");

            // A Construct unlocks its pattern's recipes on the server; a second copy is refused.
            var construct = constructs[0];
            var patternId = entMan.GetComponent<WFBlueprintConstructComponent>(construct).Pattern!.Value;
            var recipes = proto.Index(patternId).Recipes;
            Assert.That(blueprints.TryInsertConstruct(locator, (construct, entMan.GetComponent<WFBlueprintConstructComponent>(construct)), locatorUid), Is.True);
            var db = entMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
            var unlocked = db.UnlockedRecipes;
            Assert.That(recipes.All(r => unlocked.Contains(r)), Is.True, "The pattern's recipes were not unlocked.");

            var copy = blueprints.SpawnFor("WFBlueprintConstruct", proto.Index(patternId), new EntityCoordinates(ship.Owner, 0.5f, 0.5f), "wf-blueprint-construct-name");
            Assert.That(blueprints.TryInsertConstruct(locator, (copy, entMan.GetComponent<WFBlueprintConstructComponent>(copy)), locatorUid), Is.False);
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(groundMap);
            entMan.DeleteEntity(shipMap);
        });

        await pair.CleanReturnAsync();
    }
}
