#nullable enable
using System.IO;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.ShipAccess;
using Content.Server.Power.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Power;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.ShipAccess;

/// <summary>
/// Per-door rules: each rule's decision at the reader, sealing, the reader staying on for a ruled door on
/// an unlocked ship, door card lists following the allow list, rules surviving a grid save and load, and a
/// resale wiping the seller's codes, rules and seals.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDoorAccessRuleComponent))]
public sealed class ShipAccessDoorRuleTest
{
    private const string DoorProto = "AirlockShuttle";
    private const string HumanProto = "MobHuman";
    private const string CardProto = "PassengerIDCard";

    [Test]
    public async Task RulesDecideAtTheDoor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<ShipAccessReaderSystem>();
        var doors = entMan.System<SharedDoorSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, owner = default, deed = default, person = default, card = default, stranger = default, strangerCard = default;
        WFShipAccessComponent comp = default!;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            entMan.EnsureComponent<ShipAccessReaderComponent>(door).Enabled = true;
            // Bolts need power and the test map has none; without a receiver the bolt system treats the door as powered.
            entMan.RemoveComponent<ApcPowerReceiverComponent>(door);
            (owner, deed) = SpawnPersonWithCard(entMan, hands, map.GridCoords, "Ada Vance");
            GiveDeed(entMan, deed, grid);
            (person, card) = SpawnPersonWithCard(entMan, hands, map.GridCoords, "Ben Ortiz");
            (stranger, strangerCard) = SpawnPersonWithCard(entMan, hands, map.GridCoords, "Random Stranger");
            entMan.EnsureComponent<ShuttleDeedComponent>(grid);
            comp = entMan.EnsureComponent<WFShipAccessComponent>(grid);
            comp.Locked = true;
        });

        await server.WaitAssertion(() =>
        {
            var ship = new Entity<WFShipAccessComponent>(grid, comp);
            var reader = entMan.GetComponent<ShipAccessReaderComponent>(door);
            bool Allowed(EntityUid user) => readers.HasShipAccess(user, door, reader, silent: true);

            Assert.That(access.TryAddPerson(ship, person), Is.True, "Precondition: the person's card is on the allow list.");
            Assert.That(Allowed(person), Is.True, "Precondition: a listed card opens a Default door.");

            // Deed only.
            Assert.That(access.SetDoorRule(ship, door, WFDoorAccessRule.OwnerOnly), Is.True);
            Assert.That(access.SetDoorRule(ship, door, WFDoorAccessRule.OwnerOnly), Is.False, "Setting the same rule again is a no-op.");
            Assert.That(Allowed(person), Is.False, "OwnerOnly refuses a listed card.");
            Assert.That(Allowed(owner), Is.True, "OwnerOnly admits the deed.");

            // Public, even locked and even for a stranger.
            access.SetDoorRule(ship, door, WFDoorAccessRule.Public);
            Assert.That(comp.Locked, Is.True, "Precondition: the ship is still locked.");
            Assert.That(Allowed(stranger), Is.True, "Public admits a stranger on a locked ship.");

            // Sealed: bolted, and nobody, not even the deed.
            access.SetDoorRule(ship, door, WFDoorAccessRule.Sealed);
            Assert.Multiple(() =>
            {
                Assert.That(doors.IsBolted(door), Is.True, "Sealing bolts the door.");
                Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.Not.EqualTo(DoorState.Open), "A sealed door is not left open.");
                Assert.That(Allowed(owner), Is.False, "Sealed refuses the deed at the door.");
                Assert.That(Allowed(stranger), Is.False, "Sealed refuses a stranger.");
            });
            access.SetDoorRule(ship, door, WFDoorAccessRule.Default);
            Assert.That(doors.IsBolted(door), Is.False, "Leaving Sealed unbolts the door.");
            Assert.That(entMan.HasComponent<DoorBoltComponent>(door), Is.True, "Bolts the door already had are kept.");

            // Players: the door's own card list, kept in step with the allow list.
            access.SetDoorRule(ship, door, WFDoorAccessRule.Players);
            Assert.That(Allowed(person), Is.False, "Players refuses a listed card that is not picked for the door.");
            Assert.That(access.SetDoorPlayer(ship, door, strangerCard, true), Is.False, "Only allow-listed cards can be picked for a door.");
            Assert.That(access.SetDoorPlayer(ship, door, card, true), Is.True);
            Assert.That(access.SetDoorPlayer(ship, door, card, true), Is.False, "Picking a card twice is a no-op.");
            Assert.That(Allowed(person), Is.True, "Players admits a picked card.");
            Assert.That(Allowed(owner), Is.True, "Players admits the deed.");
            Assert.That(Allowed(stranger), Is.False, "Players refuses a stranger.");

            Assert.That(access.RemoveEntry(ship, entMan.GetNetEntity(card)), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<WFDoorAccessRuleComponent>(door).Players, Is.Empty, "Leaving the allow list drops the door pick.");
                Assert.That(Allowed(person), Is.False, "A dropped card is refused again.");
            });

            // Code doors admit the deed at the reader; everyone else needs the keypad.
            access.SetDoorRule(ship, door, WFDoorAccessRule.Code);
            Assert.That(Allowed(person), Is.False, "Code refuses a stranger at the reader.");
            Assert.That(Allowed(owner), Is.True, "Code admits the deed at the reader.");
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

    /// <summary>The rule is plain data that saves with the grid; the card list is round state and does not.</summary>
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

        var yaml = string.Empty;
        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<WFShipAccessComponent>(grid);
            var door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            var card = entMan.SpawnEntity(CardProto, map.GridCoords);
            var rule = entMan.EnsureComponent<WFDoorAccessRuleComponent>(door);
            rule.Rule = WFDoorAccessRule.PlayersOrCode;
            rule.Players.Add(card);
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
                Assert.That(found.Players, Is.Empty, "Card lists are round state, like guest cards, and are not saved.");
                Assert.That(found.HasOwnCode, Is.True);
            });
        });

        await server.WaitPost(() => maps.DeleteMap(loadedMap));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A used ship comes to its buyer without the seller's codes, rules or seals. A seal lifted without power
    /// leaves a bare rule that unbolts the door once power returns.
    /// </summary>
    [Test]
    public async Task ResaleClearsCodesRulesAndSeals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var access = entMan.System<WFShipAccessServerSystem>();
        var doors = entMan.System<SharedDoorSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid codeDoor = default, sealedDoor = default, darkDoor = default;
        await server.WaitPost(() =>
        {
            codeDoor = entMan.SpawnEntity(DoorProto, map.GridCoords);
            sealedDoor = entMan.SpawnEntity(DoorProto, map.GridCoords);
            darkDoor = entMan.SpawnEntity(DoorProto, map.GridCoords);
            // Without a receiver the bolt system treats a door as powered, so these seal at once.
            foreach (var door in new[] { codeDoor, sealedDoor, darkDoor })
                entMan.RemoveComponent<ApcPowerReceiverComponent>(door);

            var ship = new Entity<WFShipAccessComponent>(grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            access.SetShipCode(ship, "4321");
            access.SetDoorRule(ship, codeDoor, WFDoorAccessRule.Code);
            access.SetDoorCode(ship, codeDoor, "1234");
            access.SetDoorRule(ship, sealedDoor, WFDoorAccessRule.Sealed);
            access.SetDoorRule(ship, darkDoor, WFDoorAccessRule.Sealed);
            // The last door loses power after sealing, so its bolts can't come up at the sale.
            entMan.AddComponent<ApcPowerReceiverComponent>(darkDoor);
        });

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(doors.IsBolted(sealedDoor), Is.True, "Precondition: the sealed door is bolted.");
                Assert.That(doors.IsBolted(darkDoor), Is.True, "Precondition: the unpowered sealed door is bolted.");
            });

            entMan.System<ShipyardSystem>().StripForResale(grid);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<WFShipAccessComponent>(grid), Is.False, "The seller's access record is gone.");
                Assert.That(entMan.HasComponent<WFShipAccessCodeComponent>(grid), Is.False, "The seller's ship code is gone.");
                Assert.That(entMan.HasComponent<WFDoorCodeComponent>(codeDoor), Is.False, "The seller's door code is gone.");
                Assert.That(entMan.HasComponent<WFDoorAccessRuleComponent>(codeDoor), Is.False, "The seller's door rule is gone.");
                Assert.That(entMan.HasComponent<WFDoorAccessRuleComponent>(sealedDoor), Is.False, "The seal's rule is gone.");
                Assert.That(doors.IsBolted(sealedDoor), Is.False, "The seal's bolts are up.");
            });

            var dark = entMan.GetComponent<WFDoorAccessRuleComponent>(darkDoor);
            Assert.Multiple(() =>
            {
                Assert.That(dark.Rule, Is.EqualTo(WFDoorAccessRule.Default), "An unpowered seal is lifted as a rule.");
                Assert.That(dark.UnboltWhenPowered, Is.True, "Its bolts wait for power.");
                Assert.That(doors.IsBolted(darkDoor), Is.True, "Bolts can't come up without power.");
            });

            entMan.GetComponent<ApcPowerReceiverComponent>(darkDoor).Powered = true;
            var powered = new PowerChangedEvent(true, 0f);
            entMan.EventBus.RaiseLocalEvent(darkDoor, ref powered);
            Assert.Multiple(() =>
            {
                Assert.That(doors.IsBolted(darkDoor), Is.False, "Power brings the bolts up.");
                Assert.That(dark.UnboltWhenPowered, Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A human with a fixed name holding a blank ID card in the active hand. Server thread only.</summary>
    private static (EntityUid Person, EntityUid Card) SpawnPersonWithCard(IEntityManager entMan, SharedHandsSystem hands, EntityCoordinates coords, string name)
    {
        var person = entMan.SpawnEntity(HumanProto, coords);
        entMan.System<MetaDataSystem>().SetEntityName(person, name);
        var card = entMan.SpawnEntity(CardProto, coords);
        Assert.That(hands.TryPickupAnyHand(person, card), Is.True, $"Precondition: {name} picks up their card.");
        return (person, card);
    }

    /// <summary>Puts this ship's deed on a card; the deed's fields are write-restricted to the shipyard, so reflection stands in.</summary>
    private static void GiveDeed(IEntityManager entMan, EntityUid card, EntityUid grid)
    {
        var deed = entMan.EnsureComponent<ShuttleDeedComponent>(card);
        typeof(ShuttleDeedComponent).GetField(nameof(ShuttleDeedComponent.ShuttleUid))!.SetValue(deed, grid);
    }

    private static bool ReaderEnabled(IEntityManager entMan, EntityUid uid) =>
        entMan.TryGetComponent<ShipAccessReaderComponent>(uid, out var reader) && reader.Enabled;
}
