#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Blueprints;
using Content.Server._WF.PlanetPois;
using Content.Server.Research.Systems;
using Content.Shared._WF.Blueprints;
using Content.Shared.Maps;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Salvage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Blueprints;

/// <summary>
/// A locator hunt claims a site of its tier and hides a shard per pattern there; assembling the shards ends the hunt
/// and frees the site, and a Construct used on the R&D console unlocks its pattern's recipes on the locator's server.
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

            // A ship with a research server and an R&D console, whose locator links to the server at map init.
            shipMap = maps.CreateMap(out var shipMapId);
            var ship = maps.CreateGridEntity(shipMapId);
            maps.SetTile(ship.Owner, ship.Comp, Vector2i.Zero, floor);
            maps.SetTile(ship.Owner, ship.Comp, new Vector2i(1, 0), floor);
            var researchServer = entMan.SpawnEntity("ResearchAndDevelopmentServer", new EntityCoordinates(ship.Owner, 0.5f, 0.5f));
            var locatorUid = entMan.SpawnEntity("ComputerResearchAndDevelopment", new EntityCoordinates(ship.Owner, 1.5f, 0.5f));
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

            // Decoding: each shard becomes a Construct; the last one ends the hunt and frees the site.
            var constructs = new List<EntityUid>();
            foreach (var shard in hunt.Shards.ToList())
            {
                var comp = entMan.GetComponent<WFBlueprintShardComponent>(shard);
                var pattern = proto.Index<WFBlueprintPatternPrototype>(comp.Pattern!.Value);
                var made = blueprints.Decode(locator, (shard, comp), pattern, null);
                Assert.That(entMan.GetComponent<WFBlueprintConstructComponent>(made).Pattern, Is.EqualTo(comp.Pattern));
                constructs.Add(made);
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

            // Every tier-1 technology of the server's disciplines is free from the start.
            var supported = db.SupportedDisciplines;
            var techs = db.UnlockedTechnologies;
            var basics = proto.EnumeratePrototypes<TechnologyPrototype>()
                .Where(t => t.Tier == 1 && !t.Hidden && supported.Contains(t.Discipline)).ToList();
            Assert.That(basics, Is.Not.Empty);
            Assert.That(basics.All(t => techs.Contains(t.ID)), Is.True, "A tier-1 technology was not unlocked at map init.");

            // A tier-3 technology is hunt-only: points can't unlock it, its Construct can.
            var research = server.System<ResearchSystem>();
            var huntOnly = proto.EnumeratePrototypes<WFBlueprintPatternPrototype>()
                .First(p => p.Technology is { } t && supported.Contains(proto.Index(t).Discipline));
            var tech = proto.Index(huntOnly.Technology!.Value);
            Assert.That(tech.Tier, Is.EqualTo(3));
            Assert.That(research.IsTechnologyAvailable(db, tech), Is.False, "A hunt-only technology could be researched.");
            Assert.That(blueprints.Fits(locatorUid, huntOnly), Is.True);

            var techConstruct = blueprints.SpawnFor("WFBlueprintConstruct", huntOnly, new EntityCoordinates(ship.Owner, 0.5f, 0.5f), "wf-blueprint-construct-name");
            Assert.That(blueprints.TryInsertConstruct(locator, (techConstruct, entMan.GetComponent<WFBlueprintConstructComponent>(techConstruct)), locatorUid), Is.True);
            Assert.That(db.UnlockedTechnologies, Does.Contain(tech.ID));
            var techRecipes = db.UnlockedRecipes;
            Assert.That(tech.RecipeUnlocks.All(r => techRecipes.Contains(r)), Is.True, "The technology's recipes were not unlocked.");

            // A faction's technology isn't offered to a server without its discipline.
            var foreign = proto.EnumeratePrototypes<WFBlueprintPatternPrototype>()
                .FirstOrDefault(p => p.Technology is { } t && proto.Index(t).GetAllDisciplines().All(d => !supported.Contains(d)));
            if (foreign != null)
                Assert.That(blueprints.Fits(locatorUid, foreign), Is.False);
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(groundMap);
            entMan.DeleteEntity(shipMap);
        });

        await pair.CleanReturnAsync();
    }
}
