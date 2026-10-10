#nullable enable
using System.Numerics;
using Content.Server.Ame.Components;
using Content.Server.Materials;
using Content.Server.Power.Generator;
using Content.Server._WF.Shuttles.Systems;
using Content.Shared._FarHorizons.Materials;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared._WF.Shuttles;
using Content.Shared.Ame.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Materials;
using Content.Shared.Power.Generator;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>Installed fuel reserves use native tanks and rods without mixing their incompatible units.</summary>
[TestFixture]
public sealed class ShipFuelTelemetryTest
{
    [Test]
    public async Task InstalledSolidLiquidAndAntimatterSourcesIncludeOfflineFuelButExcludeCargo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 8; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            var transform = em.System<SharedTransformSystem>();
            var status = em.System<ShipStatusSystem>();
            var materials = em.System<MaterialStorageSystem>();
            var solutions = em.System<SharedSolutionContainerSystem>();
            var console = em.SpawnEntity("ComputerShuttle", At(0));

            Assert.That(Read().Sources, Is.Zero);
            Assert.That(Read().Available, Is.False);
            Assert.That(Read().Low, Is.False, "No fuel plant is not an empty fuel plant.");

            var solid = em.SpawnEntity("PortableGeneratorPacman", At(1));
            var adapter = em.GetComponent<SolidFuelGeneratorAdapterComponent>(solid);
            var storage = em.GetComponent<MaterialStorageComponent>(solid);
            Assert.That(materials.TrySetMaterialAmount(solid, adapter.FuelMaterial, storage.StorageLimit!.Value / 2), Is.True);
            Assert.That(Read().Sources, Is.Zero, "A loose portable generator is cargo until anchored.");
            Assert.That(transform.AnchorEntity(solid), Is.True);
            Assert.That(em.GetComponent<FuelGeneratorComponent>(solid).On, Is.False);
            Assert.That(Read().Fraction, Is.EqualTo(0.5f).Within(0.0001f), "Offline installed reserves still count.");

            var liquid = em.SpawnEntity("PortableGeneratorJrPacman", At(2));
            Assert.That(transform.AnchorEntity(liquid), Is.True);
            var tank = em.GetComponent<ChemicalFuelGeneratorAdapterComponent>(liquid);
            Assert.That(solutions.TryGetSolution(liquid, tank.SolutionName, out var solution, out var contents), Is.True);
            var half = contents!.MaxVolume / 2;
            Assert.That(solutions.TryAddReagent(solution!.Value, "WeldingFuel", half), Is.True);
            Assert.That(solutions.TryAddReagent(solution.Value, "Water", half), Is.True);
            Assert.That(Read().Sources, Is.EqualTo(2));
            Assert.That(Read().Fraction, Is.EqualTo(0.5f).Within(0.0001f), "Non-fuel chemicals do not fill the fuel gauge.");

            var ame = em.SpawnEntity("AmeController", At(3));
            Assert.That(em.GetComponent<TransformComponent>(ame).Anchored, Is.True);
            var controller = em.GetComponent<AmeControllerComponent>(ame);
            Assert.That(Read().Fraction, Is.EqualTo(1f / 3f).Within(0.0001f), "An empty installed injector contributes zero.");
            var jar = em.SpawnEntity("AmeJar", At(4));
            var fuel = em.GetComponent<AmeFuelContainerComponent>(jar);
            fuel.FuelAmount = fuel.FuelCapacity / 4;
            Assert.That(Read().Fraction, Is.EqualTo(1f / 3f).Within(0.0001f), "Loose fuel is not installed reserves.");
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(ame, controller.FuelSlot, jar, null), Is.True);
            var combined = Read();
            Assert.That(combined.Sources, Is.EqualTo(3));
            Assert.That(combined.UnknownSources, Is.Zero);
            Assert.That(combined.Fraction, Is.EqualTo(1.25f / 3f).Within(0.0001f));
            Assert.That(combined.Available, Is.True);
            Assert.That(combined.Low, Is.False);
            var lightweight = status.GetStatus(console, hullOnly: true)!;
            Assert.That(lightweight.Summary.Fuel.Fraction, Is.EqualTo(combined.Fraction));
            Assert.That(lightweight.Tiles, Is.Empty);

            var other = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            maps.SetTile(other.Owner, other.Comp, Vector2i.Zero, map.Tile.Tile);
            em.SpawnEntity("AmeController", new EntityCoordinates(other.Owner, new Vector2(0.5f, 0.5f)));
            Assert.That(Read().Sources, Is.EqualTo(3), "An adjacent grid must not change this ship's reserves.");

            Assert.That(materials.TrySetMaterialAmount(solid, adapter.FuelMaterial, 0), Is.True);
            solutions.RemoveAllSolution(solution.Value);
            fuel.FuelAmount = 0;
            Assert.That(Read().Fraction, Is.Zero);
            Assert.That(Read().Low, Is.True);
            em.RemoveComponent<MaterialStorageComponent>(solid);
            Assert.That(Read().Sources, Is.EqualTo(3));
            Assert.That(Read().UnknownSources, Is.EqualTo(1));
            Assert.That(Read().Available, Is.False);
            Assert.That(Read().Low, Is.False, "An unmeasurable source must not cause a false LOW alert.");
            transform.Unanchor(solid);
            Assert.That(Read().Sources, Is.EqualTo(2));
            Assert.That(Read().Available, Is.True);
            Assert.That(Read().Low, Is.True);

            ShipFuelSummary Read() => status.GetStatus(console)!.Summary.Fuel;
            EntityCoordinates At(int x) => new(map.Grid.Owner, new Vector2(x + 0.5f, 0.5f));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FissionMeasuresInstalledRodLifeAgainstFreshCapacityIncludingMixedFuel()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -4; x <= 4; x++)
            for (var y = -4; y <= 4; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            var status = em.System<ShipStatusSystem>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            var reactorEntity = em.SpawnEntity("NuclearReactorEmpty", map.GridCoords);
            var reactor = em.GetComponent<NuclearReactorComponent>(reactorEntity);
            Assert.That(em.GetComponent<TransformComponent>(reactorEntity).Anchored, Is.True);
            Assert.That(Read().Sources, Is.EqualTo(1));
            Assert.That(Read().Available, Is.True);
            Assert.That(Read().Fraction, Is.Zero, "An installed reactor with no rods is empty.");
            Assert.That(Read().Low, Is.True);

            var uraniumEntity = em.SpawnEntity("UraniumReactorFuelRod", map.GridCoords);
            var cerenkiteEntity = em.SpawnEntity("CerenkiteReactorFuelRod", map.GridCoords);
            var uranium = new ReactorPartComponent(em.GetComponent<ReactorPartComponent>(uraniumEntity));
            var cerenkite = new ReactorPartComponent(em.GetComponent<ReactorPartComponent>(cerenkiteEntity));
            var uraniumFresh = prototypes.Index(uranium.Material).Properties;
            var cerenkiteFresh = prototypes.Index(cerenkite.Material).Properties;
            Assert.That(Read().Fraction, Is.Zero, "Loose fresh rods do not fuel the reactor.");
            reactor.ComponentGrid[1, 1] = uranium;
            reactor.ComponentGrid[2, 2] = cerenkite;
            Assert.That(Read().Fraction, Is.EqualTo(1f), "Uninitialized rod properties use their real material prototype.");

            uranium.Properties = new MaterialProperties(uraniumFresh)
            {
                NeutronRadioactivity = uraniumFresh.NeutronRadioactivity * 0.25f,
                Radioactivity = uraniumFresh.Radioactivity * 0.25f,
            };
            cerenkite.Properties = new MaterialProperties(cerenkiteFresh)
            {
                NeutronRadioactivity = 0f,
                Radioactivity = 0f,
                FissileIsotopes = 100f,
            };
            var uraniumCapacity = 1.5f * uraniumFresh.NeutronRadioactivity + uraniumFresh.Radioactivity;
            var cerenkiteCapacity = 1.5f * cerenkiteFresh.NeutronRadioactivity + cerenkiteFresh.Radioactivity;
            Assert.That(uraniumCapacity, Is.GreaterThan(0f));
            Assert.That(cerenkiteCapacity, Is.GreaterThan(0f));
            Assert.That(Read().Fraction, Is.EqualTo(uraniumCapacity * 0.25f / (uraniumCapacity + cerenkiteCapacity)).Within(0.0001f),
                "Different rod materials are weighted by fresh activity; spent products are not usable fuel.");

            reactor.ComponentGrid[2, 2] = null;
            uranium.Properties = new MaterialProperties(uraniumFresh);
            Assert.That(uranium.Properties.NeutronRadioactivity, Is.GreaterThanOrEqualTo(0.01f));
            var before = Read().Fraction;
            uranium.Properties.NeutronRadioactivity -= 0.01f;
            uranium.Properties.Radioactivity += 0.005f;
            Assert.That(Read().Fraction, Is.EqualTo(before - 0.01f / uraniumCapacity).Within(0.0001f),
                "The native first-stage conversion retains its later ordinary reaction capacity.");

            reactor.ComponentGrid[1, 1] = null;
            reactor.ComponentGrid[2, 2] = cerenkite;
            cerenkite.Properties = new MaterialProperties(cerenkiteFresh);
            Assert.That(cerenkite.Properties.Radioactivity, Is.GreaterThanOrEqualTo(0.01f));
            cerenkite.Properties.Radioactivity -= 0.01f;
            cerenkite.Properties.FissileIsotopes += 0.005f;
            Assert.That(Read().Fraction, Is.EqualTo(1f - 0.01f / cerenkiteCapacity).Within(0.0001f),
                "The native ordinary reaction converts usable activity into spent products.");

            cerenkite.Properties.NeutronRadioactivity = cerenkiteFresh.NeutronRadioactivity * 0.2f;
            cerenkite.Properties.Radioactivity = cerenkiteFresh.Radioactivity * 0.2f;
            Assert.That(Read().Fraction, Is.EqualTo(ShipFuelSummary.LowThreshold).Within(0.0001f));
            Assert.That(Read().Low, Is.True);
            cerenkite.Melted = true;
            Assert.That(Read().Fraction, Is.Zero, "Melted rods cannot count as usable reserves.");
            cerenkite.Melted = false;
            cerenkite.Properties = null;
            reactor.Melted = true;
            Assert.That(Read().Fraction, Is.Zero);
            reactor.Melted = false;
            Assert.That(Read().Fraction, Is.EqualTo(1f));

            var solid = em.SpawnEntity("PortableGeneratorPacman", new EntityCoordinates(map.Grid.Owner, new Vector2(3.5f, 3.5f)));
            Assert.That(em.System<SharedTransformSystem>().AnchorEntity(solid), Is.True);
            Assert.That(Read().Sources, Is.EqualTo(2));
            Assert.That(Read().Fraction, Is.EqualTo(0.5f), "A full reactor and empty generator each contribute one normalized source.");

            ShipFuelSummary Read() => status.GetStatus(console, hullOnly: true)!.Summary.Fuel;
        });
        await pair.CleanReturnAsync();
    }
}
