using Content.Server.Lathe;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Power.Components;
using Content.Shared.Lathe;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.FixedPoint;
using Content.Shared.Research.Prototypes;
using Robust.Server.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Lathe;

[TestFixture]
public sealed class FabricationSiloTest
{
    [TestCase("JerryCan", "Superconductant", 250)]
    [TestCase("JerryCanWeldingFuel", "WeldingFuel", 0)]
    [TestCase("JerryCanNaniteFuel", "NaniteFuel", 0)]
    [TestCase("Beaker", "Water", 30)]
    public async Task DepositChemicalContainer(string prototype, string reagent, int added)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("MachineChemicalSilo", map.GridCoords);
            var container = entities.SpawnEntity(prototype, map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var solutions = server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetDrainableSolution(container, out var solution, out var contents), Is.True);
            if (added > 0)
                Assert.That(solutions.TryAddReagent(solution!.Value, reagent, added), Is.True);
            var expected = contents!.Volume;
            Assert.That(expected, Is.GreaterThan(FixedPoint2.Zero));
            var interaction = new InteractUsingEvent(user, container, silo, map.GridCoords);
            entities.EventBus.RaiseLocalEvent(silo, interaction);
            Assert.That(interaction.Handled, Is.True);
            var accepted = server.ProtoMan.EnumeratePrototypes<LatheRecipePrototype>().Any(recipe => recipe.Reagents.ContainsKey(reagent));
            var stock = entities.GetComponent<FabricationSiloComponent>(silo).Reagents;
            Assert.That(stock.GetValueOrDefault(reagent), Is.EqualTo(accepted ? expected : FixedPoint2.Zero));
            Assert.That(contents.Volume, Is.EqualTo(accepted ? FixedPoint2.Zero : expected),
                "Rejected chemicals must remain in the original container.");
            entities.DeleteEntity(container);
            entities.DeleteEntity(silo);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RejectNonComponentsWithoutRemovingThem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("MachinePartsSilo", map.GridCoords);
            var item = entities.SpawnEntity(null, map.GridCoords);
            var component = entities.SpawnEntity("ElectromagnetEconomy1", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var storage = entities.GetComponent<FabricationSiloComponent>(silo).Parts;
            entities.EventBus.RaiseLocalEvent(silo, new InteractUsingEvent(user, item, silo, map.GridCoords));
            Assert.That(storage.Contains(item), Is.False);
            Assert.That(entities.EntityExists(item), Is.True);
            entities.EventBus.RaiseLocalEvent(silo, new InteractUsingEvent(user, component, silo, map.GridCoords));
            Assert.That(storage.Contains(component), Is.True);
            entities.DeleteEntity(silo);
            entities.DeleteEntity(item);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IndependentLinksAndImmediateConsumption()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var machine = entities.SpawnEntity("PrecisionAssemblerEconomyCompact", map.GridCoords);
            var parts = entities.SpawnEntity("MachinePartsSilo", map.GridCoords);
            var chemicals = entities.SpawnEntity("MachineChemicalSilo", map.GridCoords);
            var silos = server.System<FabricationSiloSystem>();
            var containers = server.System<ContainerSystem>();
            var partsStore = entities.GetComponent<FabricationSiloComponent>(parts);
            var chemicalStore = entities.GetComponent<FabricationSiloComponent>(chemicals);
            var links = entities.GetComponent<FabricationSiloClientComponent>(machine);
            var netMachine = entities.GetNetEntity(machine);

            entities.GetComponent<ApcPowerReceiverComponent>(parts).Powered = true;
            entities.GetComponent<ApcPowerReceiverComponent>(chemicals).Powered = true;
            entities.EventBus.RaiseLocalEvent(parts, new ToggleFabricationSiloClientMessage(netMachine));
            entities.EventBus.RaiseLocalEvent(chemicals, new ToggleFabricationSiloClientMessage(netMachine));

            Assert.That(links.PartsSilo, Is.EqualTo(parts));
            Assert.That(links.ChemicalSilo, Is.EqualTo(chemicals));
            Assert.That(silos.GetLinkedSilo(machine, FabricationSiloKind.Parts), Is.EqualTo(parts));
            Assert.That(silos.GetLinkedSilo(machine, FabricationSiloKind.Chemicals), Is.EqualTo(chemicals));
            Assert.That(silos.GetUiState(parts, partsStore).Clients.Any(c => c.Entity == netMachine && c.Linked), Is.True);

            for (var i = 0; i < 3; i++)
                containers.Insert(entities.SpawnEntity("ElectromagnetEconomy1", map.GridCoords), partsStore.Parts);
            chemicalStore.Reagents["Superconductant"] = 30;
            var stock = silos.GetUiState(parts, partsStore).Stock;
            Assert.That(stock.Count, Is.EqualTo(1), "Identical parts must share a single inventory row.");
            Assert.That(stock[0].Label, Does.EndWith("×3"));
            Assert.That(silos.GetPartAmount(machine, "ElectromagnetEconomy1"), Is.EqualTo(3));
            Assert.That(silos.ConsumeParts(machine, "ElectromagnetEconomy1", 2), Is.EqualTo(2));
            Assert.That(silos.GetPartAmount(machine, "ElectromagnetEconomy1"), Is.EqualTo(1),
                "Consumed parts must disappear immediately, before deferred entity deletion.");
            Assert.That(silos.ConsumeReagent(machine, "Superconductant", 30), Is.True);

            entities.EventBus.RaiseLocalEvent(parts, new ToggleFabricationSiloClientMessage(netMachine));
            Assert.That(links.PartsSilo, Is.Null);
            Assert.That(links.ChemicalSilo, Is.EqualTo(chemicals));
            entities.DeleteEntity(machine);
            entities.DeleteEntity(parts);
            entities.DeleteEntity(chemicals);
        });

        await pair.CleanReturnAsync();
    }
}
