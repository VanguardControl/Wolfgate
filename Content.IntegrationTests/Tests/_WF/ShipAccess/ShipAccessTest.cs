#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.ShipAccess;
using Content.IntegrationTests.Pair;
using Content.Server._WF.ShipAccess;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Power;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WF.ShipAccess;

/// <summary>
/// The per-card ship access rules: the deed and the allow list on a locked ship, the card being the key
/// rather than the person or the account, the purchase hook that keeps every reader in step with the lock,
/// the faction card rule, a refusal playing the door's deny state, a hacked reader skipping the ship check,
/// and the console tab loading.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipAccessSystem))]
public sealed class ShipAccessTest
{
    private const string DoorProto = "AirlockShuttle";
    private const string LockerProto = "LockerFreezer";
    private const string HumanProto = "MobHuman";
    private const string CardProto = "PassengerIDCard";

    /// <summary>A company outside Mono's hard-coded USSP/Rogue/TSF list, so only the WF faction rule can admit its card.</summary>
    private const string Company = "PDV";

    [Test]
    public async Task DeedAndListedCardsGateLockedDoors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<ShipAccessReaderSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, owner = default, deed = default, person = default, card = default, stranger = default, strangerCard = default, nobody = default;
        WFShipAccessComponent comp = default!;
        await server.WaitPost(() =>
        {
            door = SpawnDoor(entMan, map.GridCoords, enabled: true);
            (owner, deed) = SpawnPersonWithCard(entMan, hands, map.GridCoords, "Ada Vance");
            GiveDeed(entMan, deed, grid);
            (person, card) = SpawnPersonWithCard(entMan, hands, map.GridCoords, "Ben Ortiz");
            (stranger, strangerCard) = SpawnPersonWithCard(entMan, hands, map.GridCoords, "Random Stranger");
            nobody = entMan.SpawnEntity(HumanProto, map.GridCoords);
            // Without a deed Mono's reader admits everyone, so a stranger can only be refused on a deeded grid.
            entMan.EnsureComponent<ShuttleDeedComponent>(grid);
            comp = entMan.EnsureComponent<WFShipAccessComponent>(grid);
            comp.Locked = true;
        });

        await server.WaitAssertion(() =>
        {
            var ship = new Entity<WFShipAccessComponent>(grid, comp);
            var reader = entMan.GetComponent<ShipAccessReaderComponent>(door);
            bool Allowed(EntityUid user) => readers.HasShipAccess(user, door, reader, silent: true);

            Assert.Multiple(() =>
            {
                Assert.That(Allowed(owner), Is.True, "The deed opens a locked ship.");
                Assert.That(Allowed(person), Is.False, "A stranger must not open a locked ship.");
                Assert.That(Allowed(nobody), Is.False, "Someone without a card must not either.");
                Assert.That(access.TryAddPerson(ship, nobody), Is.False, "There is no card to list on someone without one.");
                Assert.That(access.TryAddPerson(ship, owner), Is.False, "The deed is never listed; it already opens everything.");
            });

            Assert.That(access.TryAddPerson(ship, person), Is.True, "A person's card can be listed.");
            Assert.That(access.TryAddPerson(ship, person), Is.False, "Listing the same card twice must fail.");
            Assert.That(comp.AllowList.Single().Name, Is.EqualTo("Ben Ortiz"), "The entry shows the holder's name when the card is blank.");
            Assert.That(Allowed(person), Is.True, "A listed card opens the ship.");
            Assert.That(Allowed(stranger), Is.False, "Listing one card admits nobody else.");

            // The card is the key, not the person behind it.
            Assert.That(hands.TryDrop(person, card), Is.True, "Precondition: the person puts their card down.");
            Assert.That(Allowed(person), Is.False, "Without the listed card its holder is a stranger.");
            Assert.That(hands.TryPickupAnyHand(stranger, card), Is.True, "Precondition: the stranger picks the card up.");
            Assert.That(Allowed(stranger), Is.True, "Whoever carries the listed card gets in.");

            Assert.That(access.RemoveEntry(ship, entMan.GetNetEntity(card)), Is.True);
            Assert.That(Allowed(stranger), Is.False, "A removed card is a stranger's card again.");

            // The deed is the key to ownership in the same way.
            Assert.That(hands.TryDrop(owner, deed), Is.True, "Precondition: the owner puts the deed down.");
            Assert.That(Allowed(owner), Is.False, "Without the deed its former holder is a stranger.");
            Assert.That(hands.TryPickupAnyHand(nobody, deed), Is.True, "Precondition: someone else picks the deed up.");
            Assert.That(Allowed(nobody), Is.True, "Whoever carries the deed is the owner.");

            access.SetLocked(ship, false);
            Assert.That(Allowed(person), Is.True, "An unlocked ship is open to everyone.");
            Assert.That(Allowed(owner), Is.True, "An unlocked ship is open to the former owner too.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A refused open plays the door's deny state, and a hacked access reader or emergency access skips the ship check, as on any airlock.</summary>
    [Test]
    public async Task RefusalDeniesLikeAnAirlockAndHackingBypasses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var doors = entMan.System<SharedDoorSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, stranger = default;
        await server.WaitPost(() =>
        {
            door = SpawnDoor(entMan, map.GridCoords, enabled: true);
            // The test map has no power grid; a powered-up event stands in for it so the airlock opens and denies.
            var powered = new PowerChangedEvent(true, 0f);
            entMan.EventBus.RaiseLocalEvent(door, ref powered);
            (stranger, _) = SpawnPersonWithCard(entMan, hands, map.GridCoords, "Random Stranger");
            entMan.EnsureComponent<ShuttleDeedComponent>(grid);
            entMan.EnsureComponent<WFShipAccessComponent>(grid).Locked = true;
        });

        await server.WaitAssertion(() =>
        {
            DoorState State() => entMan.GetComponent<DoorComponent>(door).State;
            Assert.That(entMan.GetComponent<AirlockComponent>(door).Powered, Is.True, "Precondition: the airlock counts as powered.");

            Assert.That(doors.TryOpen(door, user: stranger), Is.False, "A stranger cannot open the door.");
            Assert.That(State(), Is.EqualTo(DoorState.Denying), "The refusal plays the deny state.");
            doors.SetState(door, DoorState.Closed);

            var reader = entMan.GetComponent<AccessReaderComponent>(door);
            reader.Enabled = false;
            Assert.That(doors.TryOpen(door, user: stranger), Is.True, "With the access wire cut, the ship check is skipped.");
            Assert.That(State(), Is.EqualTo(DoorState.Opening));
            doors.SetState(door, DoorState.Closed);
            reader.Enabled = true;
            Assert.That(doors.TryOpen(door, user: stranger), Is.False, "Mending the wire restores the ship check.");
            doors.SetState(door, DoorState.Closed);

            entMan.System<SharedAirlockSystem>().SetEmergencyAccess((door, entMan.GetComponent<AirlockComponent>(door)), true);
            Assert.That(entMan.GetComponent<AirlockComponent>(door).EmergencyAccess, Is.True, "Precondition: emergency access is on.");
            Assert.That(doors.TryOpen(door, user: stranger), Is.True, "Emergency access skips the ship check.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PurchaseSetsUpShipLocksReadersAndCoversLaterBuilds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        // The door's reader starts disabled, as Mono's purchase sweep leaves it.
        EntityUid door = default, buyer = default;
        await server.WaitPost(() =>
        {
            door = SpawnDoor(entMan, map.GridCoords, enabled: false);
            (buyer, _) = SpawnPersonWithCard(entMan, hands, map.GridCoords, "Ada Vance");
            entMan.EnsureComponent<ShuttleDeedComponent>(grid);
            Assert.That(entMan.HasComponent<WFShipAccessComponent>(grid), Is.False, "Precondition: no ship access before purchase.");
            entMan.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(grid, buyer));
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFShipAccessComponent>(grid), Is.True, "The purchase must add ship access.");
            var comp = entMan.GetComponent<WFShipAccessComponent>(grid);
            Assert.Multiple(() =>
            {
                Assert.That(comp.OwnerName, Is.EqualTo("Ada Vance"), "The buyer's name is kept for display.");
                Assert.That(comp.Mode, Is.EqualTo(WFShipAccessMode.Private), "A grid without a company is Private.");
                Assert.That(comp.Locked, Is.True, "wf.shipaccess.lock_new_ships locks a new ship.");
                Assert.That(ReaderEnabled(entMan, door), Is.True, "The purchase enables the existing reader.");
            });
        });

        // Doors and lockers built after the purchase get a reader that follows the lock. The locker is anchored so
        // physics cannot shove it off the one-tile grid before the lock is flipped.
        var xforms = entMan.System<SharedTransformSystem>();
        EntityUid laterDoor = default, locker = default;
        await server.WaitPost(() =>
        {
            laterDoor = entMan.SpawnEntity(DoorProto, map.GridCoords);
            locker = entMan.SpawnEntity(LockerProto, map.GridCoords);
            Assert.That(xforms.AnchorEntity(locker), Is.True, "Precondition: the locker anchors to the grid.");
        });
        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(ReaderEnabled(entMan, laterDoor), Is.True, "A door built on a locked ship gets an enabled reader.");
                Assert.That(ReaderEnabled(entMan, locker), Is.True, "A locker built on a locked ship gets an enabled reader.");
            });

            access.SetLocked((grid, entMan.GetComponent<WFShipAccessComponent>(grid)), false);
            Assert.Multiple(() =>
            {
                foreach (var uid in new[] { door, laterDoor, locker })
                    Assert.That(ReaderEnabled(entMan, uid), Is.False, $"Unlocking must disable the reader on {entMan.ToPrettyString(uid)}.");
            });
        });

        // Faction variant: a company grid goes Faction, and a card of that company opens it for an unlisted stranger.
        var readers = entMan.System<ShipAccessReaderSystem>();
        var factionMap = await pair.CreateTestMap();
        var factionGrid = factionMap.Grid.Owner;
        EntityUid factionDoor = default, npc = default, card = default;
        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<ShuttleDeedComponent>(factionGrid);
            entMan.EnsureComponent<CompanyComponent>(factionGrid).CompanyName = Company;
            factionDoor = SpawnDoor(entMan, factionMap.GridCoords, enabled: false);
            npc = entMan.SpawnEntity(HumanProto, factionMap.GridCoords);
            card = entMan.SpawnEntity(CardProto, factionMap.GridCoords);
            entMan.GetComponent<IdCardComponent>(card).CompanyName = Company;
            entMan.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(factionGrid, buyer));
        });

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFShipAccessComponent>(factionGrid);
            var reader = entMan.GetComponent<ShipAccessReaderComponent>(factionDoor);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Mode, Is.EqualTo(WFShipAccessMode.Faction), "A company grid is Faction.");
                Assert.That(comp.Locked, Is.True);
                Assert.That(readers.HasShipAccess(npc, factionDoor, reader, silent: true), Is.False, "No card, no entry.");
            });

            Assert.That(hands.TryPickupAnyHand(npc, card), Is.True, "Precondition: the stranger picks up the card.");
            Assert.That(readers.HasShipAccess(npc, factionDoor, reader, silent: true), Is.True, "A company card opens a faction ship.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The XAML loads, and with no grid the tab reads as read-only with an empty list.</summary>
    [Test]
    public async Task ScreenLoadsAndShowsEmptyState()
    {
        // The client only has entity systems while it is in game, and the screen resolves one in its constructor.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var screen = new ShipAccessScreen();
            screen.SetShuttle(null);
            screen.SetConsole(null);
            screen.Refresh();
            Assert.Multiple(() =>
            {
                Assert.That(Find<Label>(screen, "ReadOnlyLabel").Visible, Is.True, "Without a deed the tab is read-only.");
                Assert.That(Find<Label>(screen, "AllowListEmptyLabel").Visible, Is.True, "An empty allow list says so.");
            });
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The deed's ship link reaches the client, so the player carrying the deed gets the editable tab and
    /// predicts their own door opens. Without it every client saw an empty deed and the tab stayed read-only.
    /// </summary>
    [Test]
    public async Task ClientSeesTheDeedAndGetsTheEditableTab()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var (_, body) = await AttachPlayer(pair, map, "Ada Vance");

        await server.WaitPost(() =>
        {
            var card = entMan.SpawnEntity(CardProto, map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(body, card), Is.True, "Precondition: the player picks up their card.");
            GiveDeed(entMan, card, grid);
            var comp = entMan.EnsureComponent<WFShipAccessComponent>(grid);
            comp.Locked = true;
            entMan.Dirty(grid, comp);
        });
        await pair.RunTicksSync(10);

        var clientGrid = pair.ToClientUid(grid);
        await pair.Client.WaitAssertion(() =>
        {
            var clientEnt = pair.Client.EntMan;
            var local = pair.Client.Session!.AttachedEntity;
            Assert.That(local, Is.Not.Null, "Precondition: the client has its body.");
            Assert.That(clientEnt.System<WFShipAccessSystem>().HasDeedFor(local!.Value, clientGrid), Is.True, "The client knows its card holds this ship's deed.");

            using var screen = new ShipAccessScreen();
            screen.SetShuttle(clientGrid);
            screen.Refresh();
            Assert.Multiple(() =>
            {
                Assert.That(Find<Label>(screen, "ReadOnlyLabel").Visible, Is.False, "The deed holder's tab is not read-only.");
                Assert.That(Find<CheckBox>(screen, "LockedCheck").Visible, Is.True, "The deed holder can flip the lock.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Moves the client's player, with a new mind, into a named MobHuman on the map.</summary>
    private static async Task<(ICommonSession Session, EntityUid Body)> AttachPlayer(TestPair pair, TestMapData map, string name)
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
            entMan.System<MetaDataSystem>().SetEntityName(body, name);
            var mind = mindSys.CreateMind(session.UserId).Owner;
            mindSys.TransferTo(mind, body);
        });

        await pair.RunTicksSync(5);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "The player did not attach to the body.");
        return (session, body);
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

    /// <summary>
    /// Puts this ship's deed on a card, as the shipyard does at purchase. The deed's fields are write-restricted
    /// to the shipyard systems, so reflection stands in for them here.
    /// </summary>
    private static void GiveDeed(IEntityManager entMan, EntityUid card, EntityUid grid)
    {
        var deed = entMan.EnsureComponent<ShuttleDeedComponent>(card);
        typeof(ShuttleDeedComponent).GetField(nameof(ShuttleDeedComponent.ShuttleUid))!.SetValue(deed, grid);
        entMan.Dirty(card, deed);
    }

    /// <summary>A shuttle airlock with a ship access reader in the given state. Server thread only.</summary>
    private static EntityUid SpawnDoor(IEntityManager entMan, EntityCoordinates coords, bool enabled)
    {
        var door = entMan.SpawnEntity(DoorProto, coords);
        entMan.EnsureComponent<ShipAccessReaderComponent>(door).Enabled = enabled;
        return door;
    }

    private static bool ReaderEnabled(IEntityManager entMan, EntityUid uid) =>
        entMan.TryGetComponent<ShipAccessReaderComponent>(uid, out var reader) && reader.Enabled;

    private static T Find<T>(Control root, string name) where T : Control =>
        Tree(root).OfType<T>().Single(control => control.Name == name);

    private static IEnumerable<Control> Tree(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Tree(child))
            yield return descendant;
    }
}
