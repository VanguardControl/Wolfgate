#nullable enable
using System.IO;
using Content.IntegrationTests.Pair;
using Content.Server._WF.ShipAccess;
using Content.Server.Power.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WF.ShipAccess;

/// <summary>
/// Per-door rules: each rule's decision at the reader, sealing, the reader staying on for a ruled door on
/// an unlocked ship, door lists following the allow list, and rules surviving a grid save and load.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDoorAccessRuleComponent))]
public sealed class ShipAccessDoorRuleTest
{
    private const string DoorProto = "AirlockShuttle";
    private const string HumanProto = "MobHuman";

    [Test]
    public async Task RulesDecideAtTheDoor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<ShipAccessReaderSystem>();
        var doors = entMan.System<SharedDoorSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var (session, player) = await AttachPlayer(pair, map);

        EntityUid door = default, npc = default;
        WFShipAccessComponent comp = default!;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            entMan.EnsureComponent<ShipAccessReaderComponent>(door).Enabled = true;
            // Bolts need power and the test map has none; without a receiver the bolt system treats the door as powered.
            entMan.RemoveComponent<ApcPowerReceiverComponent>(door);
            npc = entMan.SpawnEntity(HumanProto, map.GridCoords);
            entMan.EnsureComponent<ShuttleDeedComponent>(grid);
            comp = entMan.EnsureComponent<WFShipAccessComponent>(grid);
            comp.OwnerUserId = new NetUserId(Guid.NewGuid());
            comp.Locked = true;
        });

        await server.WaitAssertion(() =>
        {
            var ship = new Entity<WFShipAccessComponent>(grid, comp);
            var reader = entMan.GetComponent<ShipAccessReaderComponent>(door);
            bool Allowed(EntityUid user) => readers.HasShipAccess(user, door, reader, silent: true);
            var stranger = new NetUserId(Guid.NewGuid());

            Assert.That(access.TryAddPerson(ship, player), Is.True, "Precondition: the player is on the allow list.");
            Assert.That(Allowed(player), Is.True, "Precondition: a listed player opens a Default door.");

            // Owner only.
            Assert.That(access.SetDoorRule(ship, door, WFDoorAccessRule.OwnerOnly), Is.True);
            Assert.That(access.SetDoorRule(ship, door, WFDoorAccessRule.OwnerOnly), Is.False, "Setting the same rule again is a no-op.");
            Assert.That(Allowed(player), Is.False, "OwnerOnly refuses a listed player.");
            access.SetOwner(ship, session.UserId, "owner");
            Assert.That(Allowed(player), Is.True, "OwnerOnly admits the owner.");

            // Public, even locked and even for an NPC.
            access.SetDoorRule(ship, door, WFDoorAccessRule.Public);
            Assert.That(comp.Locked, Is.True, "Precondition: the ship is still locked.");
            Assert.That(Allowed(npc), Is.True, "Public admits an NPC on a locked ship.");

            // Sealed: bolted, and nobody, not even the owner.
            access.SetDoorRule(ship, door, WFDoorAccessRule.Sealed);
            Assert.Multiple(() =>
            {
                Assert.That(doors.IsBolted(door), Is.True, "Sealing bolts the door.");
                Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.Not.EqualTo(DoorState.Open), "A sealed door is not left open.");
                Assert.That(Allowed(player), Is.False, "Sealed refuses the owner at the door.");
                Assert.That(Allowed(npc), Is.False, "Sealed refuses an NPC.");
            });
            access.SetDoorRule(ship, door, WFDoorAccessRule.Default);
            Assert.That(doors.IsBolted(door), Is.False, "Leaving Sealed unbolts the door.");
            Assert.That(entMan.HasComponent<DoorBoltComponent>(door), Is.True, "Bolts the door already had are kept.");

            // Players: the door's own list, kept in step with the allow list.
            access.SetOwner(ship, stranger, "someone else");
            access.SetDoorRule(ship, door, WFDoorAccessRule.Players);
            Assert.That(Allowed(player), Is.False, "Players refuses a listed player who is not picked for the door.");
            Assert.That(access.SetDoorPlayer(ship, door, stranger, true), Is.False, "Only allow-listed people can be picked for a door.");
            Assert.That(access.SetDoorPlayer(ship, door, session.UserId, true), Is.True);
            Assert.That(Allowed(player), Is.True, "Players admits a picked player.");
            Assert.That(Allowed(npc), Is.False, "Players refuses an NPC.");

            Assert.That(access.RemoveEntry(ship, session.UserId), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<WFDoorAccessRuleComponent>(door).Players, Is.Empty, "Leaving the allow list drops the door pick.");
                Assert.That(Allowed(player), Is.False, "A dropped player is refused again.");
            });

            // Code doors admit the owner at the reader; everyone else needs the keypad.
            access.SetDoorRule(ship, door, WFDoorAccessRule.Code);
            Assert.That(Allowed(player), Is.False, "Code refuses a stranger at the reader.");
            access.SetOwner(ship, session.UserId, "owner");
            Assert.That(Allowed(player), Is.True, "Code admits the owner at the reader.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RuleKeepsReaderEnabledOnUnlockedShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var access = entMan.System<WFShipAccessServerSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default;
        Entity<WFShipAccessComponent> ship = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            entMan.EnsureComponent<ShipAccessReaderComponent>(door).Enabled = true;
            ship = (grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            access.SetLocked(ship, false);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(ReaderEnabled(entMan, door), Is.False, "Precondition: unlocking disables the reader.");

            access.SetDoorRule(ship, door, WFDoorAccessRule.OwnerOnly);
            Assert.That(ReaderEnabled(entMan, door), Is.True, "A ruled door keeps its reader on while the ship is unlocked.");

            access.SetLocked(ship, true);
            access.SetLocked(ship, false);
            Assert.That(ReaderEnabled(entMan, door), Is.True, "Flipping the lock leaves a ruled door's reader on.");

            access.SetDoorRule(ship, door, WFDoorAccessRule.Default);
            Assert.That(ReaderEnabled(entMan, door), Is.False, "Back on Default the reader follows the lock again.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The rule component is plain data: it saves with the grid and comes back as it was.</summary>
    [Test]
    public async Task RulesSurviveGridSaveAndLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var loader = entMan.System<MapLoaderSystem>();
        var maps = entMan.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var picked = new NetUserId(Guid.NewGuid());

        var yaml = string.Empty;
        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<WFShipAccessComponent>(grid).OwnerUserId = picked;
            var door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            var rule = entMan.EnsureComponent<WFDoorAccessRuleComponent>(door);
            rule.Rule = WFDoorAccessRule.PlayersOrCode;
            rule.Players.Add(picked);
            rule.HasOwnCode = true;

            using var writer = new StringWriter();
            Assert.That(loader.TrySaveGrid(grid, writer), Is.True, "The grid saves.");
            yaml = writer.ToString();
        });

        Assert.That(yaml, Does.Contain("WFDoorAccessRule"), "The rule component is written to the save.");

        MapId loadedMap = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out loadedMap);
            using var reader = new StringReader(yaml);
            Assert.That(loader.TryLoadGrid(loadedMap, reader, "ship-access-test", out var loaded), Is.True, "The grid loads again.");

            WFDoorAccessRuleComponent? found = null;
            var query = entMan.EntityQueryEnumerator<WFDoorAccessRuleComponent, TransformComponent>();
            while (query.MoveNext(out _, out var rule, out var xform))
            {
                if (xform.GridUid == loaded!.Value.Owner)
                    found = rule;
            }

            Assert.That(found, Is.Not.Null, "The loaded grid has the ruled door.");
            Assert.Multiple(() =>
            {
                Assert.That(found!.Rule, Is.EqualTo(WFDoorAccessRule.PlayersOrCode));
                Assert.That(found.Players, Is.EqualTo(new[] { picked }));
                Assert.That(found.HasOwnCode, Is.True);
            });
        });

        await server.WaitPost(() => maps.DeleteMap(loadedMap));
        await pair.CleanReturnAsync();
    }

    /// <summary>Moves the client's player, with a new mind, into a MobHuman on the map.</summary>
    private static async Task<(ICommonSession Session, EntityUid Body)> AttachPlayer(TestPair pair, TestMapData map)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        Assert.That(pair.Client.Session, Is.Not.Null, "This test needs a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        EntityUid body = default;
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            body = entMan.SpawnEntity(HumanProto, map.GridCoords);
            var mind = mindSys.CreateMind(session.UserId).Owner;
            mindSys.TransferTo(mind, body);
        });

        await pair.RunTicksSync(5);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "The player did not attach to the body.");
        return (session, body);
    }

    private static bool ReaderEnabled(IEntityManager entMan, EntityUid uid) =>
        entMan.TryGetComponent<ShipAccessReaderComponent>(uid, out var reader) && reader.Enabled;
}
