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
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WF.ShipAccess;

/// <summary>
/// The per-person ship access rules: owner and allow list on a locked ship, the purchase hook that registers
/// the buyer and keeps every reader in step with the lock, the faction card rule, and the console tab loading.
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
    public async Task OwnerAndAllowListGateLockedDoors()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<ShipAccessReaderSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var (session, player) = await AttachPlayer(pair, map);

        EntityUid door = default, npc = default;
        WFShipAccessComponent comp = default!;
        await server.WaitPost(() =>
        {
            door = SpawnDoor(entMan, map.GridCoords, enabled: true);
            npc = entMan.SpawnEntity(HumanProto, map.GridCoords);
            // Without a deed Mono's reader admits everyone, so a stranger can only be refused on a deeded grid.
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

            Assert.Multiple(() =>
            {
                Assert.That(Allowed(player), Is.False, "A stranger must not open a locked ship.");
                Assert.That(Allowed(npc), Is.False, "An NPC must not open a locked ship.");
                Assert.That(access.TryAddPerson(ship, npc), Is.False, "An NPC has no account to list.");
            });

            Assert.That(access.TryAddPerson(ship, player), Is.True, "A player can be listed.");
            Assert.That(access.TryAddPerson(ship, player), Is.False, "Listing someone twice must fail.");
            Assert.That(Allowed(player), Is.True, "A listed player opens the ship.");

            Assert.That(access.RemoveEntry(ship, session.UserId), Is.True);
            Assert.That(Allowed(player), Is.False, "A removed player is a stranger again.");

            access.SetOwner(ship, session.UserId, "owner");
            Assert.That(Allowed(player), Is.True, "The owner opens the ship.");

            access.SetOwner(ship, new NetUserId(Guid.NewGuid()), "someone else");
            access.SetLocked(ship, false);
            Assert.That(Allowed(player), Is.True, "An unlocked ship is open to everyone.");
            Assert.That(Allowed(npc), Is.True, "An unlocked ship is open to NPCs too.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PurchaseRegistersOwnerLocksReadersAndCoversLaterBuilds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var access = entMan.System<WFShipAccessServerSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var (session, player) = await AttachPlayer(pair, map);

        // The door's reader starts disabled, as Mono's purchase sweep leaves it.
        EntityUid door = default;
        await server.WaitPost(() =>
        {
            door = SpawnDoor(entMan, map.GridCoords, enabled: false);
            entMan.EnsureComponent<ShuttleDeedComponent>(grid);
            Assert.That(entMan.HasComponent<WFShipAccessComponent>(grid), Is.False, "Precondition: no ship access before purchase.");
            entMan.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(grid, player));
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFShipAccessComponent>(grid), Is.True, "The purchase must add ship access.");
            var comp = entMan.GetComponent<WFShipAccessComponent>(grid);
            Assert.Multiple(() =>
            {
                Assert.That(comp.OwnerUserId, Is.EqualTo(session.UserId), "The buyer becomes the owner.");
                Assert.That(comp.OwnerName, Is.EqualTo(entMan.GetComponent<MetaDataComponent>(player).EntityName));
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

        // Faction variant: a company grid goes Faction, and a card of that company opens it for an unlisted NPC.
        var readers = entMan.System<ShipAccessReaderSystem>();
        var hands = entMan.System<SharedHandsSystem>();
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
            entMan.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(factionGrid, player));
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

            Assert.That(hands.TryPickupAnyHand(npc, card), Is.True, "Precondition: the NPC picks up the card.");
            Assert.That(readers.HasShipAccess(npc, factionDoor, reader, silent: true), Is.True, "A company card opens a faction ship.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The XAML loads, and with no grid the tab reads as read-only with nothing to claim and an empty list.</summary>
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
                Assert.That(Find<Label>(screen, "ReadOnlyLabel").Visible, Is.True, "Without a grid the tab is read-only.");
                Assert.That(Find<Control>(screen, "ClaimBox").Visible, Is.False, "Nothing to claim without a grid.");
                Assert.That(Find<Label>(screen, "AllowListEmptyLabel").Visible, Is.True, "An empty allow list says so.");
            });
        });
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
