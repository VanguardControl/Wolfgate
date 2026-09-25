#nullable enable
using System.Collections.Generic;
using Content.Server.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Species;

/// <summary>
/// The Avali reagent rules ported from HardLight: amoxla and ammonia are safe for Avali and poison humans,
/// saline poisons Avali and not humans, and the Avali auto-injector is loaded and assemblable.
/// </summary>
[TestFixture]
public sealed class AvaliChemistryTest
{
    /// <summary>Five seconds: enough heart metabolism passes for every reagent to take effect.</summary>
    private const int MetabolismTicks = 150;

    private const string Avali = "MobAvali";
    private const string Human = "MobHuman";

    [Test]
    public async Task ReagentsFollowSpeciesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var bloodstream = entMan.System<BloodstreamSystem>();

        var subjects = new Dictionary<(string Species, string Reagent), EntityUid>();
        await server.WaitPost(() =>
        {
            foreach (var species in new[] { Avali, Human })
            {
                foreach (var reagent in new[] { "Amoxla", "Saline", "Ammonia" })
                {
                    var mob = entMan.SpawnEntity(species, map.GridCoords);
                    Assert.That(bloodstream.TryAddToChemicals(mob, new Solution(reagent, FixedPoint2.New(10))),
                        Is.True, $"Could not inject {reagent} into {species}.");
                    subjects[(species, reagent)] = mob;
                }
            }
        });

        await server.WaitRunTicks(MetabolismTicks);

        await server.WaitAssertion(() =>
        {
            FixedPoint2 Damage(string species, string reagent, string type)
            {
                var damage = entMan.GetComponent<DamageableComponent>(subjects[(species, reagent)]).Damage;
                return damage.DamageDict.GetValueOrDefault(type);
            }

            Assert.Multiple(() =>
            {
                Assert.That(Damage(Avali, "Amoxla", "Poison"), Is.EqualTo(FixedPoint2.Zero), "Amoxla poisoned an Avali.");
                Assert.That(Damage(Human, "Amoxla", "Poison"), Is.GreaterThan(FixedPoint2.Zero), "Amoxla didn't poison a human.");
                Assert.That(Damage(Avali, "Saline", "Poison"), Is.GreaterThan(FixedPoint2.Zero), "Saline didn't poison an Avali.");
                Assert.That(Damage(Human, "Saline", "Poison"), Is.EqualTo(FixedPoint2.Zero), "Saline poisoned a human.");
                Assert.That(Damage(Avali, "Ammonia", "Caustic"), Is.EqualTo(FixedPoint2.Zero), "Ammonia burned an Avali.");
                Assert.That(Damage(Human, "Ammonia", "Caustic"), Is.GreaterThan(FixedPoint2.Zero), "Ammonia didn't burn a human.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AutoInjectorTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var map = await pair.CreateTestMap();
        var solutions = entMan.System<SharedSolutionContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var pen = entMan.SpawnEntity("AvaliAutoInjector", map.GridCoords);
            Assert.That(solutions.TryGetSolution(pen, "pen", out _, out var solution), Is.True, "The pen has no solution.");
            Assert.Multiple(() =>
            {
                Assert.That(solution!.GetTotalPrototypeQuantity("Amoxla"), Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(solution.GetTotalPrototypeQuantity("TranexamicAcid"), Is.EqualTo(FixedPoint2.New(5)));
            });

            var pack = protoMan.Index(new ProtoId<LatheRecipePackPrototype>("MedicalAssemblerStatic"));
            Assert.That(pack.Recipes, Does.Contain(new ProtoId<LatheRecipePrototype>("AvaliAutoInjector")),
                "The medical assembler can't make the Avali auto-injector.");
            Assert.That(protoMan.Index(new ProtoId<LatheRecipePrototype>("AvaliAutoInjector")).Result?.Id, Is.EqualTo("AvaliAutoInjector"));
        });

        await pair.CleanReturnAsync();
    }
}
