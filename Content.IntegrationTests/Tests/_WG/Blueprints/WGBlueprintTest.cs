#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Blueprints;
using Content.Server._WG.PlanetPois;
using Content.Server.Research.Systems;
using Content.Shared._WG.Blueprints;
using Content.Shared.Maps;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Salvage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Blueprints;

/// <summary>
/// A locator hunt claims a site of its tier and hides a shard per pattern there; assembling the shards ends the hunt
/// and frees the site, and a Construct used on the R&D console unlocks its pattern's recipes on the locator's server.
/// </summary>
[TestFixture]
[TestOf(typeof(WGBlueprintSystem))]
public sealed class WGBlueprintTest
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
        var blueprints = server.System<WGBlueprintSystem>();

        var groundMap = EntityUid.Invalid;
        var shipMap = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var floor = new Tile(tileDefs["FloorSteel"].TileId);

            // A ground with one Moderate site and one Extreme site, standing in for a world's POI record.
            groundMap = maps.CreateMap(out var groundMapId);
            var ground = maps.CreateGridEntity(groundMapId);
            var record = entMan.AddComponent<WGPlanetPoiComponent>(ground.Owner);
            record.NetworkReady = true;
            foreach (var (left, rating) in new[] { (0, DifficultyRating.Moderate), (20, DifficultyRating.Extreme) })
            {
                var site = new WGPlanetPoiSite
                {
                    Name = "wg-blueprint-locator-title",
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
            var locator = new Entity<WGBlueprintLocatorComponent>(locatorUid, entMan.GetComponent<WGBlueprintLocatorComponent>(locatorUid));
            locator.Comp.Fees = new[] { 0, 0, 0 };

            // Patterns of two tiers in one request are refused.
            Assert.That(blueprints.TryLocate(locator, locatorUid, new List<string> { "WGPatternOmnitool", "WGPatternHandTeleporter" }, out _), Is.False);

            Assert.That(blueprints.TryLocate(locator, locatorUid, new List<string> { "WGPatternOmnitool", "WGPatternJetpackBlack" }, out var error), Is.True, error);
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
            Assert.That(blueprints.TryLocate(locator, locatorUid, new List<string> { "WGPatternHandTeleporter" }, out _), Is.False);

            // Decoding: each shard becomes a Construct; the last one ends the hunt and frees the site.
            var constructs = new List<EntityUid>();
            foreach (var shard in hunt.Shards.ToList())
            {
                var comp = entMan.GetComponent<WGBlueprintShardComponent>(shard);
                var pattern = proto.Index<WGBlueprintPatternPrototype>(comp.Pattern!.Value);
                var made = blueprints.Decode(locator, (shard, comp), pattern, null);
                Assert.That(entMan.GetComponent<WGBlueprintConstructComponent>(made).Pattern, Is.EqualTo(comp.Pattern));
                constructs.Add(made);
                entMan.DeleteEntity(shard);
            }

            Assert.That(locator.Comp.Hunt, Is.Null, "The hunt outlived its shards.");
            Assert.That(record.Sites[0].BlueprintClaim, Is.Null, "The site stayed claimed.");

            // The fee comes out of the server's research points first.
            var research = server.System<ResearchSystem>();
            var serverComp = entMan.GetComponent<ResearchServerComponent>(researchServer);
            locator.Comp.Fees = new[] { 1000, 1000, 1000 };
            locator.Comp.NextHunt = TimeSpan.Zero;
            research.ModifyServerPoints(researchServer, 1500 - serverComp.Points, serverComp);
            Assert.That(blueprints.TryLocate(locator, locatorUid, new List<string> { "WGPatternOmnitool" }, out error), Is.True, error);
            Assert.That(serverComp.Points, Is.EqualTo(500), "The fee wasn't taken from research points.");
            blueprints.Abandon(locator);

            // A Construct unlocks its pattern's recipes on the server; a second copy is refused.
            var construct = constructs[0];
            var patternId = entMan.GetComponent<WGBlueprintConstructComponent>(construct).Pattern!.Value;
            var recipes = proto.Index(patternId).Recipes;
            Assert.That(blueprints.TryInsertConstruct(locator, (construct, entMan.GetComponent<WGBlueprintConstructComponent>(construct)), locatorUid), Is.True);
            var db = entMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
            var unlocked = db.UnlockedRecipes;
            Assert.That(recipes.All(r => unlocked.Contains(r)), Is.True, "The pattern's recipes were not unlocked.");

            var copy = blueprints.SpawnFor("WGBlueprintConstruct", proto.Index(patternId), new EntityCoordinates(ship.Owner, 0.5f, 0.5f), "wg-blueprint-construct-name");
            Assert.That(blueprints.TryInsertConstruct(locator, (copy, entMan.GetComponent<WGBlueprintConstructComponent>(copy)), locatorUid), Is.False);

            // Every tier-1 technology of the server's disciplines is free from the start.
            var supported = db.SupportedDisciplines;
            var techs = db.UnlockedTechnologies;
            var basics = proto.EnumeratePrototypes<TechnologyPrototype>()
                .Where(t => t.Tier == 1 && !t.Hidden && supported.Contains(t.Discipline)).ToList();
            Assert.That(basics, Is.Not.Empty);
            Assert.That(basics.All(t => techs.Contains(t.ID)), Is.True, "A tier-1 technology was not unlocked at map init.");

            // Every technology past tier 1 has a pattern, so points unlock nothing.
            var patternTechs = proto.EnumeratePrototypes<WGBlueprintPatternPrototype>()
                .Where(p => p.Technology != null).Select(p => p.Technology!.Value.Id).ToHashSet();
            var missing = proto.EnumeratePrototypes<TechnologyPrototype>()
                .Where(t => t.Tier > 1 && !t.Hidden && !patternTechs.Contains(t.ID)).Select(t => t.ID).ToList();
            Assert.That(missing, Is.Empty, "Technologies without a pattern; rerun Tools/_WG/Blueprints/gen_techweb_patterns.py.");

            // A hunt-only technology: points can't unlock it, its Construct can.
            var huntOnly = proto.EnumeratePrototypes<WGBlueprintPatternPrototype>()
                .First(p => p.Technology is { } t && supported.Contains(proto.Index(t).Discipline));
            var tech = proto.Index(huntOnly.Technology!.Value);
            Assert.That(tech.Tier, Is.GreaterThan(1));
            Assert.That(research.IsTechnologyAvailable(db, tech), Is.False, "A hunt-only technology could be researched.");
            Assert.That(blueprints.Fits(locatorUid, huntOnly), Is.True);

            // A technology whose prerequisites aren't known can't be located yet.
            var blocked = proto.EnumeratePrototypes<WGBlueprintPatternPrototype>()
                .FirstOrDefault(p => p.Technology != null && blueprints.Fits(locatorUid, p) && !blueprints.PrerequisitesKnown(locatorUid, p));
            if (blocked != null)
            {
                locator.Comp.NextHunt = TimeSpan.Zero;
                locator.Comp.Fees = new[] { 0, 0, 0 };
                Assert.That(blueprints.TryLocate(locator, locatorUid, new List<string> { blocked.ID }, out error), Is.False);
                Assert.That(error, Is.EqualTo("wg-blueprint-locator-prerequisites"));
            }

            // Decoding arrays: none on this ship, and the speed-up has a floor.
            Assert.That(blueprints.CountArrays(locatorUid), Is.Zero);
            Assert.That(WGBlueprintDecode.Scale(0), Is.EqualTo(1f));
            Assert.That(WGBlueprintDecode.Scale(20), Is.EqualTo(0.4f));

            var techConstruct = blueprints.SpawnFor("WGBlueprintConstruct", huntOnly, new EntityCoordinates(ship.Owner, 0.5f, 0.5f), "wg-blueprint-construct-name");
            Assert.That(blueprints.TryInsertConstruct(locator, (techConstruct, entMan.GetComponent<WGBlueprintConstructComponent>(techConstruct)), locatorUid), Is.True);
            Assert.That(db.UnlockedTechnologies, Does.Contain(tech.ID));
            var techRecipes = db.UnlockedRecipes;
            Assert.That(tech.RecipeUnlocks.All(r => techRecipes.Contains(r)), Is.True, "The technology's recipes were not unlocked.");

            // A faction's technology isn't offered to a server without its discipline.
            var foreign = proto.EnumeratePrototypes<WGBlueprintPatternPrototype>()
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
