#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ShipCommand;
using Content.Server.Anomaly;
using Content.Server.Carrying;
using Content.Server.Salvage.Magnet;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Doors.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.AdminVessels;

/// <summary>The fixes from the Lupercal's second playtest (v0.30), on a freshly spawned ship.</summary>
[TestFixture]
public sealed class LupercalPlaytest2Test
{
    [Test]
    public async Task SecondPlaytestFixes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();
        EntityUid grid = default;

        await server.WaitPost(() =>
        {
            var vessel = protos.Index<VesselPrototype>("WGLupercal");
            Assert.That(server.System<AdminVesselSpawnSystem>().TrySpawnVessel(vessel, map.MapId, new Vector2(120, 0), null, out var spawned, createStation: true), Is.True);
            grid = spawned!.Value;
        });
        // Long enough for every door's auto-close and the crew's first rounds.
        await pair.RunTicksSync(300);

        await server.WaitAssertion(() =>
        {
            using var scope = Assert.EnterMultipleScope();
            var xforms = server.System<SharedTransformSystem>();
            var maps = server.System<SharedMapSystem>();
            var gridComp = em.GetComponent<MapGridComponent>(grid);

            // The secure windoors stand shut.
            foreach (var door in OnGrid<DoorComponent>(em, grid))
            {
                var proto = Proto(em, door.Owner) ?? string.Empty;
                if (proto.StartsWith("Windoor"))
                    Assert.That(door.Comp.State, Is.EqualTo(DoorState.Closed), $"{proto} at {em.GetComponent<TransformComponent>(door.Owner).LocalPosition} is {door.Comp.State}.");
            }

            // The magnet has offers, and faces space: the way it pulls leaves the ship within a few tiles.
            var station = server.System<StationSystem>().GetOwningStation(grid);
            Assert.That(station, Is.Not.Null);
            Assert.That(em.TryGetComponent<SalvageMagnetDataComponent>(station!.Value, out var data), Is.True, "The station keeps magnet offers.");
            Assert.That(data!.Offered, Is.Not.Empty, "The magnet has wrecks on offer.");
            var magnet = OnGrid<SalvageMagnetComponent>(em, grid).Single();
            var magnetXform = em.GetComponent<TransformComponent>(magnet.Owner);
            var pull = (magnetXform.LocalRotation - MathF.PI / 2).ToVec();
            var tile = maps.TileIndicesFor(grid, gridComp, magnetXform.Coordinates);
            var outside = Enumerable.Range(1, 4).Any(i =>
            {
                var at = tile + new Vector2i((int) MathF.Round(pull.X * i), (int) MathF.Round(pull.Y * i));
                return !maps.TryGetTileRef(grid, gridComp, at, out var tileRef) || tileRef.Tile.IsEmpty;
            });
            Assert.That(outside, Is.True, $"The magnet pulls toward {pull}, into the ship.");

            // Directional fans hold the air at the doors to space and the core's gates.
            Assert.That(OfProto(em, grid, "AtmosDeviceFanDirectional"), Has.Count.GreaterThanOrEqualTo(12));

            // Science has its anomaly generator, whose anomalies come in on the chamber's marker: that grid's own, never
            // one on another grid, and a grid with none falls back to a random tile.
            Assert.That(OfProto(em, grid, "MachineAnomalyGenerator"), Has.Count.EqualTo(1));
            var marker = OfProto(em, grid, "WGAnomalyChamberMarker").Single();
            em.SpawnEntity("WGAnomalyChamberMarker", map.GridCoords);
            var anomalies = server.System<AnomalySystem>();
            var before = OfProto(em, grid, "Paper").ToHashSet();
            Assert.That(anomalies.TrySpawnInChamber(grid, "Paper"), Is.True, "The Lupercal's chamber takes the anomaly.");
            var spawned = OfProto(em, grid, "Paper").Single(p => !before.Contains(p));
            Assert.That(em.GetComponent<TransformComponent>(spawned).Coordinates.Position,
                Is.EqualTo(em.GetComponent<TransformComponent>(marker).Coordinates.Position), "It lands on the Lupercal's marker.");
            var bare = server.System<SharedMapSystem>().CreateGridEntity(map.MapId);
            Assert.That(anomalies.TrySpawnInChamber(bare, "Paper"), Is.False, "A grid with no chamber falls back.");

            // The janitor's closet has its vendor; Medical has its defibrillators.
            Assert.That(OfProto(em, grid, "VendingMachineJaniDrobe"), Is.Not.Empty);
            Assert.That(OfProto(em, grid, "Defibrillator").Count + OfProto(em, grid, "DefibrillatorCompact").Count, Is.GreaterThanOrEqualTo(6));

            // The counter traders are the ship's own: the crew let them be in the locked-down docks.
            var command = server.System<WGShipCommandSystem>();
            var roster = em.GetComponent<WGShipRosterComponent>(grid);
            var marine = roster.Slots.Values.First(s => s.Role.Id == "WGCrewMarine" && s.Occupant != null).Occupant!.Value;
            var shipCommand = (grid, em.GetComponent<WGShipCommandComponent>(grid));
            foreach (var counter in roster.Slots.Values.Where(s => s.Kind == WGShipRosterKind.Counter && s.Occupant != null))
                Assert.That(command.Welcome(shipCommand, marine, counter.Occupant!.Value, out _), Is.True, $"{counter.What} is welcome at its counter.");

            // A crewman's rounds stop at the ship's walls.
            var friendlyFire = server.System<WGCrewFriendlyFireSystem>();
            var wall = OfProto(em, grid, "WallPlastitaniumIndestructible").First();
            Assert.That(friendlyFire.Protected(marine, wall), Is.False, "A marine's bullet passes through a wall.");
        });

        // A paramedic carries a patient rather than dragging them.
        await server.WaitAssertion(() =>
        {
            var roster = em.GetComponent<WGShipRosterComponent>(grid);
            var medic = roster.Slots.Values.First(s => s.Role.Id == "WGCrewParamedic" && s.Occupant != null).Occupant!.Value;
            var patient = em.SpawnEntity("MobHuman", em.GetComponent<TransformComponent>(medic).Coordinates);
            var carrying = server.System<CarryingSystem>();
            Assert.That(carrying.TryCrewCarry(medic, patient), Is.True, "The paramedic picks the patient up.");
            Assert.That(em.GetComponent<BeingCarriedComponent>(patient).Carrier, Is.EqualTo(medic));
            carrying.DropCarried(medic, patient);
            em.DeleteEntity(patient);
        });

        await pair.CleanReturnAsync();
    }

    private static List<Entity<T>> OnGrid<T>(IEntityManager em, EntityUid grid) where T : IComponent
    {
        var found = new List<Entity<T>>();
        var query = em.EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid == grid)
                found.Add((uid, comp));
        }

        return found;
    }

    private static List<EntityUid> OfProto(IEntityManager em, EntityUid grid, string proto)
    {
        var found = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.GridUid == grid && meta.EntityPrototype?.ID == proto)
                found.Add(uid);
        }

        return found;
    }

    private static string? Proto(IEntityManager em, EntityUid uid) => em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
}
