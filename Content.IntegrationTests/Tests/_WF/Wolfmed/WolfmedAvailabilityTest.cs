#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Research.Prototypes;
using Content.Shared.VendingMachines;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W7: everything Wolfmed added has to be obtainable in a normal round. A treatment that exists only in a
/// prototype file is a wound with no cure, so each new item is checked to be vended or printed, each new
/// reagent to have a reaction that makes it, and each fill W7 touched to actually hold what it declares.
/// </summary>
/// <remarks>
/// The spawn half is the second reason this test exists: <c>StorageFill</c> logs an error and drops the
/// entry when a kit is full, and an integration test fails on any error log. Putting the skin graft in the
/// burn kit is therefore checked here rather than discovered in a round - the same overflow is what
/// withdrew PROTO E's tourniquet from <c>MedkitAdvancedFilled</c> in WP12-9.
/// </remarks>
[TestFixture]
public sealed class WolfmedAvailabilityTest : GameTest
{
    /// <summary>Items Wolfmed added that a player has to be able to get hold of.</summary>
    private static readonly string[] ObtainableItems =
    [
        "WolfmedSkinGraft",
        "SpaceacillinChemistryBottle",
    ];

    /// <summary>Reagents Wolfmed added. Each needs a reaction chemistry can run.</summary>
    private static readonly string[] ObtainableReagents = ["Spaceacillin"];

    /// <summary>Fills W7 changed or relies on, spawned so an overflow fails here.</summary>
    private static readonly (string Entity, string Content)[] Fills =
    [
        ("MedkitBurnFilled", "WolfmedSkinGraft"),
        ("CrateMedicalSurgery", "WolfmedSkinGraft"),
        ("CrateMedicalSupplies", "SpaceacillinChemistryBottle"),
    ];

    /// <summary>Reachability: some vending inventory or lathe recipe names every new item.</summary>
    [Test]
    public async Task EveryNewItemIsObtainableTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var vended = prototypes.EnumeratePrototypes<VendingMachineInventoryPrototype>()
                .SelectMany(inventory => inventory.StartingInventory.Keys)
                .ToHashSet();

            var printed = prototypes.EnumeratePrototypes<LatheRecipePrototype>()
                .Where(recipe => recipe.Result != null)
                .Select(recipe => recipe.Result!.Value.Id)
                .ToHashSet();

            Assert.Multiple(() =>
            {
                foreach (var item in ObtainableItems)
                {
                    Assert.That(prototypes.HasIndex<EntityPrototype>(item), Is.True, $"{item} does not exist.");
                    Assert.That(vended.Contains(item) || printed.Contains(item), Is.True,
                        $"{item} is in no vending inventory and no lathe recipe: a player cannot get one, " +
                        "so whatever it treats has no treatment.");
                }
            });
        });
    }

    /// <summary>A new reagent needs a reaction chemistry can run.</summary>
    [Test]
    public async Task EveryNewReagentIsObtainableTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var produced = prototypes.EnumeratePrototypes<ReactionPrototype>()
                .SelectMany(reaction => reaction.Products.Keys)
                .ToHashSet();

            Assert.Multiple(() =>
            {
                foreach (var reagent in ObtainableReagents)
                {
                    Assert.That(prototypes.HasIndex<ReagentPrototype>(reagent), Is.True, $"{reagent} does not exist.");
                    Assert.That(produced, Does.Contain(reagent),
                        $"no reaction makes {reagent}, so chemistry cannot supply it.");
                }
            });
        });
    }

    /// <summary>
    /// The fills spawn cleanly and hold what they say. An overflowing kit logs an error, which fails the
    /// test on its own; the content check catches a silently dropped entry.
    /// </summary>
    [Test]
    public async Task FillsContainWhatTheyDeclareTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var containers = entities.System<SharedContainerSystem>();
            Assert.Multiple(() =>
            {
                foreach (var (id, content) in Fills)
                {
                    var spawned = entities.SpawnEntity(id, map.GridCoords);
                    var held = new List<string>();
                    foreach (var container in containers.GetAllContainers(spawned))
                    {
                        foreach (var entity in container.ContainedEntities)
                        {
                            if (entities.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID is { } prototype)
                                held.Add(prototype);
                        }
                    }

                    Assert.That(held, Does.Contain(content),
                        $"{id} does not contain {content}: the fill overflowed or the entry was dropped.");
                    entities.DeleteEntity(spawned);
                }
            });
        });
    }
}
