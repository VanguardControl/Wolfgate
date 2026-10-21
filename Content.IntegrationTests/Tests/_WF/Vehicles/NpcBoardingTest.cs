#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Strangers;
using Content.Server._WF.Vehicles.Npc;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>A hostile NPC climbs into an unlocked vehicle after whoever's inside, and attacks a locked one from outside.</summary>
[TestFixture]
[TestOf(typeof(WFNpcBoardingSystem))]
public sealed class NpcBoardingTest
{
    private static readonly Vector2 VehicleSpot = new(5.5f, 5.5f);

    /// <summary>The interior test chassis on a floor with gravity, the player sat inside it and a mugger he's angered outside.</summary>
    private static async Task<(EntityUid Vehicle, EntityUid Player, EntityUid Mugger)> Rig(TestPair pair, bool locked)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid vehicle = default, player = default, mugger = default;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -8; x < 16; x++)
            for (var y = -4; y < 16; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            var gravity = entMan.EnsureComponent<Content.Shared.Gravity.GravityComponent>(map.Grid.Owner);
            gravity.Enabled = true;
            gravity.Inherent = true;
            entMan.Dirty(map.Grid.Owner, gravity);

            vehicle = entMan.SpawnEntity("WFInteriorTestVehicle", new EntityCoordinates(map.Grid.Owner, VehicleSpot));
            player = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, VehicleSpot + new Vector2(1f, 0f)));
            server.PlayerMan.SetAttachedEntity(pair.Player!, player);
        });
        await server.WaitRunTicks(5);
        await server.WaitPost(() => server.System<SharedInteractionSystem>().InteractionActivate(player, vehicle));
        await server.WaitRunTicks(30);

        await server.WaitPost(() =>
        {
            Assert.That(entMan.HasComponent<VehicleInteriorOccupantComponent>(player), "The player never got in.");
            if (locked)
            {
#pragma warning disable RA0002 // The test locks the vehicle directly.
                entMan.GetComponent<VehicleLockComponent>(vehicle).Locked = true;
#pragma warning restore RA0002
            }
            mugger = entMan.SpawnEntity("WFStrangerMugger", new EntityCoordinates(map.Grid.Owner, -5.5f, 5.5f));
        });
        await server.WaitRunTicks(2);
        await server.WaitPost(() =>
        {
            // He takes a swipe from the player, from inside the cab.
            var damage = new DamageSpecifier(server.ProtoMan.Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Blunt"), 5);
            server.System<DamageableSystem>().TryChangeDamage(mugger, damage, origin: player);
        });
        return (vehicle, player, mugger);
    }

    [Test]
    public async Task HeClimbsIntoAnUnlockedVehicle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (vehicle, player, mugger) = await Rig(pair, locked: false);
        await pair.RunSeconds(15);
        await server.WaitAssertion(() =>
        {
            var hostiles = entMan.GetComponent<FactionExceptionComponent>(mugger).Hostiles;
            Assert.That(hostiles, Does.Contain(player), "Hit from the cab, he should go for the player.");
            Assert.That(hostiles, Does.Contain(vehicle), "...and the vehicle he's sat in.");
            Assert.That(entMan.TryGetComponent<VehicleInteriorOccupantComponent>(mugger, out var occupant) && occupant.Vehicle == vehicle,
                Is.True, "He should have climbed in after the player.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ALockedVehicleKeepsHimOut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (vehicle, _, mugger) = await Rig(pair, locked: true);
        await pair.RunSeconds(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<VehicleInteriorOccupantComponent>(mugger), Is.False, "A locked vehicle should keep him out.");
            Assert.That(entMan.GetComponent<FactionExceptionComponent>(mugger).Hostiles, Does.Contain(vehicle), "He should be going for the hull.");
            Assert.That(entMan.GetComponent<WFStrangerComponent>(mugger).Hostile, Is.True);
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(mugger), Is.True, "A player in the cab should keep him awake.");
        });
        await pair.CleanReturnAsync();
    }
}
