#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.SafetyDepositBox;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared._WF.SafetyDepositBox.Events;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Mind;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SafetyDepositBox;

/// <summary>
/// A safety deposit box and its contents never exist both in the world and in the database: an item that won't load
/// doesn't stop a withdrawal, a box with a request running takes no second one, and a box that was ejected or changed
/// during its deposit stays withdrawn.
/// </summary>
[TestFixture]
[TestOf(typeof(SafetyDepositBoxSystem))]
public sealed class SafetyDepositBoxDuplicationTest
{
    private const string ConsoleProto = "SafetyDepositConsole";
    private const string BoxProto = "WFSafetyDepositTestBox";
    private const string PlayerProto = "MobHuman";
    private const string MissingProto = "WFSafetyDepositTestRemovedItem";

    private static readonly string[] ItemProtos = { "Crowbar", "Wrench" };

    // The real boxes log an error on first use: their blacklist names components that don't exist.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFSafetyDepositTestBox
  parent: BaseStorageItem
  components:
  - type: Storage
    maxItemSize: Normal
    grid:
    - 0,0,3,3
  - type: SafetyDepositBox
    cost: 1
  - type: Tag
    tags:
    - SafetyDepositBox
";

    [Test]
    public async Task WithdrawSkipsUnloadableItem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var bench = await Bench.Create(pair);

        // A real deposit, so the saved data is what the game writes.
        var boxId = await bench.BuyBox();
        await bench.SlotBox(boxId);
        await bench.Send(new SafetyDepositDepositMessage());

        var saved = await bench.Db.GetSafetyDepositBox(boxId);
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.Items, Has.Count.EqualTo(ItemProtos.Length), "The deposit didn't save the box's items.");
        Assert.That(saved.LastWithdrawn, Is.Null, "The deposit didn't mark the box as stored.");
        Assert.That(await bench.BoxesInWorld(boxId), Is.Empty, "The deposited box is still in the world.");

        // Put an item whose prototype no longer exists ahead of the real ones.
        var data = saved.Items.Select(item => item.EntityData).ToList();
        var broken = data[0];
        foreach (var proto in ItemProtos)
        {
            broken = broken.Replace($"proto: {proto}", $"proto: {MissingProto}");
        }

        Assert.That(broken, Is.Not.EqualTo(data[0]), "The saved item data no longer names its prototype as 'proto: X'.");
        data.Insert(0, broken);
        await bench.Db.DepositSafetyDepositBoxItems(boxId, data);

        // The loader and the system both log the item that can't load as an error, which would fail the test.
        var failureLevel = pair.ServerLogHandler.FailureLevel;
        pair.ServerLogHandler.FailureLevel = LogLevel.Fatal;
        try
        {
            await bench.Send(new SafetyDepositWithdrawMessage(boxId));
        }
        finally
        {
            pair.ServerLogHandler.FailureLevel = failureLevel;
        }

        var boxes = await bench.BoxesInWorld(boxId);
        Assert.That(boxes, Has.Count.EqualTo(1), "The withdrawal didn't hand out exactly one box.");
        Assert.That(await bench.ItemCount(boxes[0]), Is.EqualTo(ItemProtos.Length),
            "The items after the broken one didn't come out.");

        var after = await bench.Db.GetSafetyDepositBox(boxId);
        Assert.That(after!.Items, Is.Empty, "The database still holds the items of a box that is in the world.");
        Assert.That(after.LastWithdrawn, Is.Not.Null, "The box isn't marked as withdrawn.");

        await bench.Send(new SafetyDepositWithdrawMessage(boxId));
        Assert.That(await bench.BoxesInWorld(boxId), Has.Count.EqualTo(1), "The box came out a second time.");

        await bench.Db.DeleteSafetyDepositBox(boxId);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PendingBoxDropsRequests()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var bench = await Bench.Create(pair);
        var server = pair.Server;
        var system = bench.EntMan.System<SafetyDepositBoxSystem>();

        var boxId = await bench.BuyBox();
        var box = await bench.SlotBox(boxId);

        // Deposit.
        var gate = new TaskCompletionSource();
        await server.WaitPost(() => system.RunBoxRequest(boxId, () => gate.Task));
        await bench.Send(new SafetyDepositDepositMessage());

        Assert.That(await bench.SlottedBox(), Is.EqualTo(box), "A deposit ran while another request held the box.");
        Assert.That((await bench.Db.GetSafetyDepositBox(boxId))!.Items, Is.Empty,
            "A deposit saved items while another request held the box.");

        await bench.Release(gate);
        await bench.Send(new SafetyDepositDepositMessage());

        Assert.That(await bench.BoxesInWorld(boxId), Is.Empty, "The deposit didn't run once the box was free.");
        Assert.That((await bench.Db.GetSafetyDepositBox(boxId))!.Items, Has.Count.EqualTo(ItemProtos.Length));

        // Withdraw.
        gate = new TaskCompletionSource();
        await server.WaitPost(() => system.RunBoxRequest(boxId, () => gate.Task));
        await bench.Send(new SafetyDepositWithdrawMessage(boxId));

        Assert.That(await bench.BoxesInWorld(boxId), Is.Empty, "A withdrawal ran while another request held the box.");

        await bench.Release(gate);
        await bench.Send(new SafetyDepositWithdrawMessage(boxId));

        var boxes = await bench.BoxesInWorld(boxId);
        Assert.That(boxes, Has.Count.EqualTo(1), "The withdrawal didn't run once the box was free.");
        Assert.That(await bench.ItemCount(boxes[0]), Is.EqualTo(ItemProtos.Length));

        // Reclaim: a box is lost once it was withdrawn in another round.
        await bench.Db.ClearSafetyDepositBoxItems(boxId, bench.Round + 1);
        var owned = await bench.OwnedBoxIds();

        gate = new TaskCompletionSource();
        await server.WaitPost(() => system.RunBoxRequest(boxId, () => gate.Task));
        await bench.Send(new SafetyDepositReclaimMessage(boxId));

        Assert.That(await bench.OwnedBoxIds(), Is.EquivalentTo(owned), "A reclaim ran while another request held the box.");

        await bench.Release(gate);
        await bench.Send(new SafetyDepositReclaimMessage(boxId));

        var replaced = await bench.OwnedBoxIds();
        Assert.That(replaced, Does.Not.Contain(boxId), "The reclaim didn't run once the box was free.");
        Assert.That(replaced, Has.Count.EqualTo(owned.Count), "The reclaim didn't swap the lost box for one new box.");

        var replacement = replaced.Except(owned).Single();
        Assert.That(await bench.BoxesInWorld(replacement), Has.Count.EqualTo(1));

        await bench.Send(new SafetyDepositReclaimMessage(boxId));
        Assert.That(await bench.OwnedBoxIds(), Is.EquivalentTo(replaced), "A reclaimed box was reclaimed again.");

        await bench.Db.DeleteSafetyDepositBox(replacement);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChangedDepositIsReverted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var bench = await Bench.Create(pair);
        var server = pair.Server;
        var entMan = bench.EntMan;
        var system = entMan.System<SafetyDepositBoxSystem>();

        var boxId = await bench.BuyBox();
        var box = await bench.SlotBox(boxId);

        SafetyDepositConsoleComponent console = default!;
        SafetyDepositBoxComponent boxComp = default!;
        StorageComponent storage = default!;
        var saved = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            console = entMan.GetComponent<SafetyDepositConsoleComponent>(bench.Console);
            boxComp = entMan.GetComponent<SafetyDepositBoxComponent>(box);
            storage = entMan.GetComponent<StorageComponent>(box);
        });

        // The database answers at once in tests, so nothing can get between a deposit's save and its check.
        // Each case sets up what the check would find after the save and runs it.
        async Task<bool> SaveThenCheck(Action change)
        {
            await server.WaitPost(() =>
            {
                saved.Clear();
                saved.AddRange(storage.Container.ContainedEntities);
            });
            await bench.Db.DepositSafetyDepositBoxItems(boxId, saved.Select(item => item.ToString()).ToList());

            Task<bool> check = default!;
            await server.WaitPost(() =>
            {
                change();
                check = system.RevertChangedDeposit(bench.Console, console, bench.Body, box, boxComp, storage, saved);
            });

            return await check;
        }

        async Task AssertRecord(bool stored, string message)
        {
            var record = await bench.Db.GetSafetyDepositBox(boxId);
            Assert.That(record!.Items, Has.Count.EqualTo(stored ? saved.Count : 0), message);
            Assert.That(record.LastWithdrawnRoundId, Is.EqualTo(stored ? null : (int?) bench.Round), message);
        }

        Assert.That(await SaveThenCheck(() => { }), Is.False, "An untouched deposit was reverted.");
        await AssertRecord(true, "An untouched deposit lost its save.");

        var changed = await SaveThenCheck(() =>
            entMan.System<SharedContainerSystem>().Remove(storage.Container.ContainedEntities[0], storage.Container));
        Assert.That(changed, Is.True, "A box that lost an item during its deposit was still deposited.");
        await AssertRecord(false, "A box that lost an item during its deposit kept its save.");

        var ejected = await SaveThenCheck(() =>
            Assert.That(entMan.System<ItemSlotsSystem>().TryEject(bench.Console, console.BoxSlot, null, out _), Is.True));
        Assert.That(ejected, Is.True, "A box ejected during its deposit was still deposited.");
        await AssertRecord(false, "A box ejected during its deposit kept its save.");
        Assert.That(await bench.BoxesInWorld(boxId), Is.EquivalentTo(new[] { box }));

        // A deleted box took its contents with it, so its save is the only copy.
        Assert.That(await SaveThenCheck(() => entMan.DeleteEntity(box)), Is.False, "A deleted box's deposit was reverted.");
        await AssertRecord(true, "A deleted box lost its save.");

        await bench.Db.DeleteSafetyDepositBox(boxId);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A console, a body the player owns and the calls the tests share.
    /// </summary>
    private sealed class Bench
    {
        public TestPair Pair = default!;
        public IEntityManager EntMan = default!;
        public IServerDbManager Db = default!;
        public EntityCoordinates Coords;
        public EntityUid Console;
        public EntityUid Body;
        public Guid UserId;
        public int Character;
        public int Round;

        public static async Task<Bench> Create(TestPair pair)
        {
            var server = pair.Server;
            var map = await pair.CreateTestMap();
            var player = pair.Player!;

            var bench = new Bench
            {
                Pair = pair,
                EntMan = server.EntMan,
                Db = server.ResolveDependency<IServerDbManager>(),
                Coords = map.GridCoords,
                UserId = player.UserId.UserId,
            };

            await server.WaitAssertion(() =>
            {
                var entMan = bench.EntMan;
                var mindSys = entMan.System<SharedMindSystem>();

                bench.Console = entMan.SpawnEntity(ConsoleProto, bench.Coords);
                bench.Body = entMan.SpawnEntity(PlayerProto, bench.Coords);
                mindSys.TransferTo(mindSys.CreateMind(player.UserId).Owner, bench.Body);
                Assert.That(player.AttachedEntity, Is.EqualTo(bench.Body), "The player didn't get the body.");

                bench.Character = server.ResolveDependency<IServerPreferencesManager>()
                    .GetPreferences(player.UserId)
                    .SelectedCharacterIndex;
                bench.Round = entMan.System<GameTicker>().RoundId;
            });

            return bench;
        }

        /// <summary>
        /// Sends a console message from the player and lets it finish.
        /// </summary>
        public async Task Send<T>(T message) where T : BoundUserInterfaceMessage
        {
            await Pair.Server.WaitPost(() =>
            {
                message.Actor = Body;
                EntMan.EventBus.RaiseLocalEvent(Console, message);
            });

            await Pair.RunTicksSync(5);
        }

        /// <summary>
        /// Finishes a request started with <see cref="SafetyDepositBoxSystem.RunBoxRequest"/>.
        /// </summary>
        public async Task Release(TaskCompletionSource gate)
        {
            await Pair.Server.WaitPost(gate.SetResult);
            await Pair.RunTicksSync(5);
        }

        /// <summary>
        /// Adds a box record that is out in the world, as a purchase leaves it.
        /// </summary>
        public async Task<Guid> BuyBox()
        {
            var box = await Db.PurchaseSafetyDepositBox(UserId, Character, "Tester", BoxProto);
            await Db.ClearSafetyDepositBoxItems(box.BoxId, Round);
            return box.BoxId;
        }

        /// <summary>
        /// Spawns the player's box for a record, fills it and puts it in the console.
        /// </summary>
        public async Task<EntityUid> SlotBox(Guid boxId)
        {
            var box = EntityUid.Invalid;
            await Pair.Server.WaitAssertion(() =>
            {
                box = EntMan.SpawnEntity(BoxProto, Coords);
                var comp = EntMan.GetComponent<SafetyDepositBoxComponent>(box);
                comp.BoxId = boxId;
                comp.OwnerId = UserId;
                comp.CharacterIndex = Character;

                var storage = EntMan.System<SharedStorageSystem>();
                foreach (var proto in ItemProtos)
                {
                    var item = EntMan.SpawnEntity(proto, Coords);
                    Assert.That(storage.Insert(box, item, out _, playSound: false), Is.True,
                        $"{proto} doesn't go in {BoxProto}; pick an item that does.");
                }

                var console = EntMan.GetComponent<SafetyDepositConsoleComponent>(Console);
                Assert.That(EntMan.System<ItemSlotsSystem>().TryInsert(Console, console.BoxSlot, box, null), Is.True,
                    "The console didn't take the box.");
            });

            return box;
        }

        public async Task<EntityUid?> SlottedBox()
        {
            EntityUid? box = null;
            await Pair.Server.WaitPost(() => box = EntMan.GetComponent<SafetyDepositConsoleComponent>(Console).BoxSlot.Item);
            return box;
        }

        public async Task<List<EntityUid>> BoxesInWorld(Guid boxId)
        {
            var found = new List<EntityUid>();
            await Pair.Server.WaitPost(() =>
            {
                var query = EntMan.AllEntityQueryEnumerator<SafetyDepositBoxComponent>();
                while (query.MoveNext(out var uid, out var comp))
                {
                    if (comp.BoxId == boxId)
                        found.Add(uid);
                }
            });

            return found;
        }

        public async Task<int> ItemCount(EntityUid box)
        {
            var count = 0;
            await Pair.Server.WaitPost(() => count = EntMan.GetComponent<StorageComponent>(box).Container.ContainedEntities.Count);
            return count;
        }

        public async Task<List<Guid>> OwnedBoxIds()
        {
            var boxes = await Db.GetPlayerSafetyDepositBoxes(UserId, Character);
            return boxes.Select(box => box.BoxId).ToList();
        }
    }
}
