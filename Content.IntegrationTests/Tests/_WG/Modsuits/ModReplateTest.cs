using System.Collections.Generic;
using System.Linq;
using Content.Shared._WG.Modsuits;
using Content.Shared._WG.Modsuits.Components;
using Content.Shared._WG.Modsuits.Modules;
using Content.Shared._WG.Modsuits.Systems;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Wires;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Modsuits;

/// <summary>Checks a suit can be regrown as another pattern at runtime, and when it refuses.</summary>
[TestFixture]
[TestOf(typeof(ModSuitSystem))]
public sealed class ModReplateTest
{
    private static readonly ProtoId<ModPatternPrototype> Loader = "WGPatternLoader";
    private static readonly ProtoId<ModPatternPrototype> Engineer = "WGPatternEngineer";

    /// <summary>Installed on a Standard suit, newest last, to push it past the Loader's complexity cap.</summary>
    private static readonly string[] ExtraModules =
    {
        "ModsuitModuleJetpack",
        "ModsuitModuleMagboots",
        "ModsuitModuleGps",
        "ModsuitModuleHealthAnalyzer",
        "ModsuitModuleTether",
    };

    [Test]
    public async Task ReplateSwapsPartsAndShedsNewestModules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var wires = entMan.System<SharedWiresSystem>();
        var storage = entMan.System<SharedStorageSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var protoMan = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            var suit = entMan.SpawnEntity("ClothingModsuitStandardPowerCell", coords);
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            var ent = (suit, comp);

            Assert.That(wires.TogglePanel(suit, entMan.GetComponent<WiresPanelComponent>(suit), true, null));

            var installed = new List<EntityUid>();
            foreach (var proto in ExtraModules)
            {
                var module = entMan.SpawnEntity(proto, coords);
                if (modSuit.TryInstallModule(ent, module, null))
                    installed.Add(module);
                else
                    entMan.DeleteEntity(module);
            }

            var loader = protoMan.Index(Loader);
            var cap = protoMan.Index(loader.Theme).ComplexityMax;
            Assert.That(modSuit.GetComplexity(ent), Is.GreaterThan(cap), "the test needs a suit over the Loader's cap");

            var storageModule = comp.ModulesContainer.ContainedEntities.Single(e => entMan.HasComponent<ModStorageModuleComponent>(e));
            var cargo = entMan.SpawnEntity("Wrench", coords);
            Assert.That(storage.Insert(suit, cargo, out _, playSound: false), "the suit's storage should take a wrench");

            var oldParts = loader.Parts.Keys.Select(slot => modSuit.GetPart(ent, slot)).ToList();
            Assert.That(oldParts, Has.None.Null);

            // Newest removable modules first, until the rest fit; the storage module never goes.
            var expected = new List<EntityUid>();
            var complexity = modSuit.GetComplexity(ent);
            foreach (var module in comp.ModulesContainer.ContainedEntities.Reverse())
            {
                if (complexity <= cap)
                    break;
                var moduleComp = entMan.GetComponent<ModModuleComponent>(module);
                if (!moduleComp.Removable || module == storageModule)
                    continue;
                expected.Add(module);
                complexity -= moduleComp.Complexity;
            }

            var removed = new List<EntityUid>();
            Assert.That(modSuit.TryReplate(ent, Loader, out var refusal, removed), $"replate refused: {refusal}");

            Assert.Multiple(() =>
            {
                Assert.That(comp.Theme, Is.EqualTo(loader.Theme));
                Assert.That(comp.Pattern, Is.EqualTo(Loader));
                foreach (var part in oldParts)
                {
                    Assert.That(entMan.Deleted(part!.Value), "old parts should be deleted");
                }

                Assert.That(comp.PartEntities, Has.Count.EqualTo(loader.Parts.Count));
                foreach (var (slot, proto) in loader.Parts)
                {
                    Assert.That(modSuit.GetPart(ent, slot) is { } part
                                && entMan.GetComponent<MetaDataComponent>(part).EntityPrototype?.ID == proto,
                        $"the {slot} part should be {proto}");
                }

                var inbuilt = protoMan.Index(loader.Theme).InbuiltModules.Concat(loader.InbuiltModules).Select(p => p.Id).ToList();
                var builtIn = comp.ModulesContainer.ContainedEntities
                    .Where(e => !entMan.GetComponent<ModModuleComponent>(e).Removable)
                    .Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype!.ID)
                    .ToList();
                Assert.That(builtIn, Is.EquivalentTo(inbuilt), "inbuilt modules should be the Loader's");

                Assert.That(removed, Is.EqualTo(expected), "modules over the cap should leave newest first");
                Assert.That(modSuit.GetComplexity(ent), Is.LessThanOrEqualTo(cap));
                var modules = comp.ModulesContainer.ContainedEntities.ToList();
                Assert.That(modules, Does.Contain(storageModule), "the storage module should stay");
                foreach (var module in removed)
                {
                    Assert.That(entMan.GetComponent<ModModuleComponent>(module).Suit, Is.Null);
                    Assert.That(modules, Does.Not.Contain(module));
                }

                Assert.That(containers.TryGetContainingContainer(cargo, out var holder) && holder.Owner == suit,
                    "storage contents should survive the replate");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReplateRefusesWhileDeployedOrActive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var inventory = entMan.System<InventorySystem>();
        EntityUid human = default;
        EntityUid suit = default;
        EntityUid gloves = default;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            human = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity("ClothingModsuitStandardPowerCell", coords);
            gloves = entMan.SpawnEntity("ClothingHandsGlovesColorYellow", coords);
            Assert.That(inventory.TryEquip(human, gloves, "gloves", silent: true, force: true));
            Assert.That(inventory.TryEquip(human, suit, "back", silent: true, force: true));

            var ent = (suit, entMan.GetComponent<ModSuitComponent>(suit));
            Assert.That(modSuit.CanReplate(ent, "WGPatternDoesNotExist"), Is.EqualTo(ModReplateRefusal.UnknownPattern));
            modSuit.ToggleAllParts(ent, human);
            Assert.That(modSuit.CanReplate(ent, Engineer), Is.EqualTo(ModReplateRefusal.Busy), "a queued deploy should refuse");
        });

        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var ent = (suit, entMan.GetComponent<ModSuitComponent>(suit));
            Assert.That(modSuit.AllPartsDeployed(ent));
            Assert.That(modSuit.CanReplate(ent, Engineer), Is.EqualTo(ModReplateRefusal.PartsOut));
            Assert.That(modSuit.TryReplate(ent, Engineer, out var refusal), Is.False);
            Assert.That(refusal, Is.EqualTo(ModReplateRefusal.PartsOut));

            modSuit.ForceActivate(ent);
            Assert.That(modSuit.CanReplate(ent, Engineer), Is.EqualTo(ModReplateRefusal.NotInactive));
            modSuit.ForceDeactivate(ent, true);
            modSuit.ToggleAllParts(ent, human);
        });

        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            var ent = (suit, comp);
            Assert.That(inventory.TryGetSlotEntity(human, "gloves", out var worn) && worn == gloves,
                "gloves stashed by the gauntlets should come back on retract");

            Assert.That(modSuit.TryReplate(ent, Engineer, out var refusal), $"replate refused: {refusal}");
            Assert.That(comp.Theme, Is.EqualTo(server.ProtoMan.Index(Engineer).Theme));
            Assert.That(inventory.TryGetSlotEntity(human, "gloves", out worn) && worn == gloves,
                "replating should not touch worn clothing");
            Assert.That(comp.Wearer, Is.EqualTo(human), "the suit stays worn through a replate");
        });

        // The new parts deploy like the old ones did.
        await server.WaitAssertion(() =>
        {
            var ent = (suit, entMan.GetComponent<ModSuitComponent>(suit));
            modSuit.ToggleAllParts(ent, human);
        });

        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var ent = (suit, entMan.GetComponent<ModSuitComponent>(suit));
            Assert.That(modSuit.AllPartsDeployed(ent), "the replated parts should deploy");
        });

        await pair.CleanReturnAsync();
    }
}
