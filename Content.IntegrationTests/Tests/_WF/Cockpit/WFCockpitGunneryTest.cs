#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using Content.Client.Shuttles.UI;
using Robust.Client.UserInterface;
using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._WF.Cockpit;
using Content.Server._WF.CombatConsole;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Mono.FireControl;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._WF.Cockpit;
using Content.Shared._WF.CombatConsole;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Exercises cockpit gunnery through the real helm subscription and existing group, flare and fire handlers.</summary>
[TestFixture]
public sealed class WFCockpitGunneryTest
{
    [Test]
    public async Task NearbyUnopenedConsoleUsesPhysicalReachAndRecoversAfterPowerLoss()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid actor = default, seat = default, helm = default, gun = default, server = default, groupedWeapon = default;
        NetEntity gunNet = default, weaponNet = default;
        ShuttleConsoleWindow? clientWindow = null;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 3; x++)
            for (var y = 0; y < 3; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            var seatPosition = new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 0.5f));
            actor = em.SpawnEntity("MobHuman", seatPosition);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            seat = em.SpawnEntity("ChairPilotSeat", seatPosition);
            helm = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 1.5f)));
            gun = em.SpawnEntity("ComputerGunneryConsole", new EntityCoordinates(map.Grid.Owner, new Vector2(2.5f, 1.5f)));
            server = em.SpawnEntity("GunneryServerLow", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 2.5f)));
            foreach (var powered in new[] { helm, gun, server })
                em.GetComponent<ApcPowerReceiverComponent>(powered).Powered = true;
            em.RemoveComponent<AccessReaderComponent>(helm);
            em.RemoveComponent<AccessReaderComponent>(gun);
            var reader = em.AddComponent<AccessReaderComponent>(gun);
            reader.AccessLists.Add(new HashSet<ProtoId<AccessLevelPrototype>> { "Captain" });
            var ui = em.System<SharedUserInterfaceSystem>();
            var cockpit = em.System<WFCockpitGunnerySystem>();
            var transforms = em.System<SharedTransformSystem>();
            var interaction = em.System<SharedInteractionSystem>();
            transforms.SetCoordinates(seat, em.GetComponent<TransformComponent>(seat),
                new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 0.3f)), unanchor: false);
            em.EnsureComponent<PilotComponent>(actor);
            em.System<ShuttleConsoleSystem>().AddPilot(helm, actor, em.GetComponent<ShuttleConsoleComponent>(helm));
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
            Assert.That(em.System<SharedBuckleSystem>().TryBuckle(actor, actor, seat), Is.True);
            Assert.That(Vector2.Distance(transforms.GetWorldPosition(actor), transforms.GetWorldPosition(gun)),
                Is.GreaterThan(SharedInteractionSystem.InteractionRange), "The chair placement puts the console center beyond the old cutoff.");
            Assert.That(interaction.InRangeAndAccessible(actor, gun), Is.True,
                "The diagonal console is physically reachable using the normal fixture-distance and line-of-sight rules.");
            var control = em.GetComponent<FireControlConsoleComponent>(gun);
            Assert.That(control.ConnectedServer, Is.Null, "The gunnery console has not been opened or manually linked.");
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key, new WFCockpitGunnerySessionMessage(true, true) { Actor = actor });
            Assert.That(cockpit.GetConsole(actor), Is.Null, "Physical proximity must not bypass the console access reader.");
            Assert.That(control.ConnectedServer, Is.Null, "An unauthorized candidate must not trigger server discovery.");
            reader.Enabled = false;
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun), "The active cockpit discovers a reachable unopened console when access becomes available.");
            Assert.That(control.ConnectedServer, Is.EqualTo(server));
            Assert.That(em.GetComponent<FireControlServerComponent>(server).Consoles, Does.Contain(gun));
            Assert.That(ui.IsUiOpen(gun, FireControlConsoleUiKey.Key, actor), Is.False);
            var helmPower = em.GetComponent<ApcPowerReceiverComponent>(helm);
            helmPower.Powered = false;
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.Null);
            Assert.That(cockpit.TryCommand(actor, helm, em.GetNetEntity(gun), new WFDispenseFlaresMessage()), Is.False);
            helmPower.Powered = true;
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun), "Power restoration recovers the existing cockpit intent without a second session request.");
            ui.CloseUi(helm, ShuttleConsoleUiKey.Key, actor);
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.Null, "Closing the helm still ends the cockpit session.");
            // Keep the network fixture powered without an APC changing it during subsequent ticks.
            foreach (var entity in new[] { helm, gun, server })
                em.RemoveComponent<ApcPowerReceiverComponent>(entity);
            groupedWeapon = em.SpawnEntity(null, map.GridCoords);
            em.AddComponent<FireControllableComponent>(groupedWeapon).ControllingServer = server;
            em.GetComponent<FireControlServerComponent>(server).Controlled.Add(groupedWeapon);
            em.System<FireControlSystem>().WfRefreshConsole(gun);
            gunNet = em.GetNetEntity(gun);
            weaponNet = em.GetNetEntity(groupedWeapon);
            em.EnsureComponent<PilotComponent>(actor);
            em.System<ShuttleConsoleSystem>().AddPilot(helm, actor, em.GetComponent<ShuttleConsoleComponent>(helm));
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            static IEnumerable<Control> Descendants(Control root)
            {
                yield return root;
                foreach (var child in root.Children)
                foreach (var nested in Descendants(child))
                    yield return nested;
            }
            clientWindow = Descendants(pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot)
                .OfType<ShuttleConsoleWindow>().Single();
            clientWindow.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(true));
            clientWindow.WfSendCockpitGunnery(new WFCockpitGunneryCommandMessage(gunNet,
                new WFSaveWeaponGroupMessage(3, new List<NetEntity> { weaponNet })));
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<WFCockpitGunnerySystem>().GetConsole(actor), Is.EqualTo(gun),
                "The real client BUI must establish a discovered link with the network sender as actor.");
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(gun).Groups.ContainsKey(3), Is.False,
                "A client command sent over the network while in FLIGHT must be rejected.");
        });
        await pair.Client.WaitAssertion(() =>
            clientWindow!.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(true, true)));
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<WFCockpitGunnerySystem>().CanOperate(actor, gun), Is.True,
                "The delivered GUNS mode message must authorize the attached, seated operator before its first command.");
            Assert.That(em.GetComponent<FireControlServerComponent>(server).Controlled, Does.Contain(groupedWeapon),
                "The network fixture's weapon must remain connected while its command travels to the server.");
        });
        await pair.Client.WaitAssertion(() =>
            clientWindow!.WfSendCockpitGunnery(new WFCockpitGunneryCommandMessage(gunNet,
                new WFSaveWeaponGroupMessage(3, new List<NetEntity> { weaponNet }))));
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(gun).Groups.TryGetValue(3, out var saved), Is.True,
                "The authorized group command must reach the server through the live client BUI.");
            Assert.That(saved, Is.EquivalentTo(new[] { groupedWeapon }),
                "A real window command must cross client networking, inherit its sender identity, and reach the authorized server handler.");
        });
        await pair.Client.WaitAssertion(() =>
            clientWindow!.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(true)));
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
            Assert.That(em.System<WFCockpitGunnerySystem>().CanOperate(actor, gun), Is.False));
        await pair.Client.WaitAssertion(() =>
        {
            clientWindow!.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(true, true));
            clientWindow.WfSendCockpitGunnery(new WFCockpitGunneryCommandMessage(gunNet,
                new WFSaveWeaponGroupMessage(2, new List<NetEntity> { weaponNet })));
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(gun).Groups.TryGetValue(2, out var saved), Is.True,
                "Switching to GUNS and saving a group within one client tick must retain message order.");
            Assert.That(saved, Is.EquivalentTo(new[] { groupedWeapon }));
            em.System<SharedUserInterfaceSystem>().CloseUi(helm, ShuttleConsoleUiKey.Key, actor);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            foreach (var entity in new[] { actor, seat, helm, gun, server, groupedWeapon })
                em.DeleteEntity(entity);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SeatedPilotUsesOnlyTheReachableAuthorizedConsole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(2, 0), map.Tile.Tile);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(3, 0), map.Tile.Tile);
            var actor = em.SpawnEntity("MobHuman", map.GridCoords);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            var helm = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            var seat = em.SpawnEntity("ChairPilotSeat", map.GridCoords);
            var gun = em.SpawnEntity("ComputerGunneryConsole", map.GridCoords);
            var serverUid = em.SpawnEntity(null, map.GridCoords);
            var server = em.AddComponent<FireControlServerComponent>(serverUid);
            server.ConnectedGrid = map.Grid.Owner;
            server.ProcessingPower = 100;
            em.EnsureComponent<FireControlGridComponent>(map.Grid.Owner).ControllingServer = serverUid;
            server.Consoles.Add(gun);
            var control = em.GetComponent<FireControlConsoleComponent>(gun);
            control.ConnectedServer = serverUid;
            var gunPower = em.GetComponent<ApcPowerReceiverComponent>(gun);
            var helmPower = em.GetComponent<ApcPowerReceiverComponent>(helm);
            gunPower.Powered = true;
            helmPower.Powered = true;
            em.RemoveComponent<AccessReaderComponent>(gun);
            em.RemoveComponent<AccessReaderComponent>(helm);
            em.EnsureComponent<CrewedShuttleComponent>(map.Grid.Owner);
            var helms = em.System<ShuttleConsoleSystem>();
            var cockpit = em.System<WFCockpitGunnerySystem>();
            var ui = em.System<SharedUserInterfaceSystem>();
            var transforms = em.System<SharedTransformSystem>();
            var buckles = em.System<SharedBuckleSystem>();
            em.EnsureComponent<PilotComponent>(actor);
            helms.AddPilot(helm, actor, em.GetComponent<ShuttleConsoleComponent>(helm));
            Assert.That(cockpit.SetSession(actor, helm, true, true), Is.False, "A live helm UI and pilot seat are both required.");
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
            Assert.That(cockpit.SetSession(actor, helm, true, true), Is.False, "Standing pilots cannot acquire gunnery.");
            Assert.That(buckles.TryBuckle(actor, actor, seat), Is.True);
            var normalOpen = new ActivatableUIOpenAttemptEvent(actor);
            em.EventBus.RaiseLocalEvent(gun, normalOpen);
            Assert.That(normalOpen.Cancelled, Is.True, "The ordinary crewed-ship two-window restriction remains intact.");
            var gunAccess = em.AddComponent<AccessReaderComponent>(gun);
            var helmAccess = em.AddComponent<AccessReaderComponent>(helm);
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key, new WFCockpitGunnerySessionMessage(true, true) { Actor = actor });
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun));
            Assert.That(ui.IsUiOpen(gun, FireControlConsoleUiKey.Key, actor), Is.False, "The cockpit must not open a second gunnery BUI.");
            Assert.That(cockpit.GetActors(gun), Does.Contain(actor), "Existing crew handoff must see the cockpit operator.");
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key, new WFCockpitGunnerySessionMessage(true) { Actor = actor });
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun), "FLIGHT retains discovery and telemetry.");
            Assert.That(cockpit.GetActors(gun), Is.Empty, "FLIGHT must leave the gunner's console unclaimed.");
            Assert.That(cockpit.TryCommand(actor, helm, em.GetNetEntity(gun), new WFDispenseFlaresMessage()), Is.False,
                "A forged weapon action in FLIGHT cannot operate guns or countermeasures.");
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key, new WFCockpitGunnerySessionMessage(true, true) { Actor = actor });
            Assert.That(cockpit.GetActors(gun), Does.Contain(actor));
            var sharedState = em.System<FireControlSystem>().WfCockpitState(gun);
            for (var repeat = 0; repeat < 10; repeat++)
                Assert.That(em.System<FireControlSystem>().WfCockpitState(gun), Is.SameAs(sharedState),
                    "Linked cockpit viewers share the current periodic snapshot.");
            var snapshotSettings = em.GetComponent<WFCombatConsoleComponent>(gun);
            snapshotSettings.NextTelemetry = TimeSpan.Zero;
            Assert.That(em.System<FireControlSystem>().WfCockpitState(gun), Is.SameAs(sharedState),
                "An unchanged lightweight refresh must preserve the published snapshot.");
            snapshotSettings.NextTelemetry = snapshotSettings.NextRadarTelemetry = TimeSpan.Zero;
            Assert.That(em.System<FireControlSystem>().WfCockpitState(gun), Is.SameAs(sharedState),
                "Even a full radar refresh must not republish identical state.");

            Assert.That(gunAccess.AccessLog.Count, Is.EqualTo(1), "Acquiring the gunnery link records one successful access.");
            Assert.That(helmAccess.AccessLog.Count, Is.EqualTo(1));
            for (var refresh = 0; refresh < 25; refresh++)
            {
                Assert.That(cockpit.CanOperate(actor, gun), Is.True);
                cockpit.Update(0.3f);
            }
            Assert.That(gunAccess.AccessLog.Count, Is.EqualTo(1), "Continuous authorization must not overwrite the access history.");
            Assert.That(helmAccess.AccessLog.Count, Is.EqualTo(1));

            var weapon = em.SpawnEntity("WeaponTurretL85Autocannon", new EntityCoordinates(map.Grid.Owner, new Vector2(3.5f, 0.5f)));
            var slots = em.System<ItemSlotsSystem>();
            Assert.That(slots.TryEject(weapon, "gun_magazine", null, out var infiniteMagazine), Is.True);
            em.DeleteEntity(infiniteMagazine!.Value);
            var finiteMagazine = em.SpawnEntity("Magazine20mmAS", map.GridCoords);
            Assert.That(slots.TryInsert(weapon, "gun_magazine", finiteMagazine, null), Is.True,
                "Use the compatible finite loader so a real shot measurably consumes ammunition.");
            var fireControl = em.GetComponent<FireControllableComponent>(weapon);
            fireControl.ControllingServer = serverUid;
            server.Controlled.Add(weapon);
            var foreign = em.SpawnEntity(null, map.GridCoords);
            var foreignControl = em.AddComponent<FireControllableComponent>(foreign);
            var flare = em.SpawnEntity("WeaponTurretFlare", new EntityCoordinates(map.Grid.Owner, new Vector2(2.5f, 0.5f)));
            em.GetComponent<FireControllableComponent>(flare).ControllingServer = serverUid;
            server.Controlled.Add(flare);
            Assert.Multiple(() =>
            {
                Assert.That(em.System<SharedWFCockpitSystem>().CanEnter(actor, helm), Is.True, "Weapon setup must preserve the seated pilot.");
                Assert.That(ui.IsUiOpen(helm, ShuttleConsoleUiKey.Key, actor), Is.True, "Weapon setup must preserve the helm subscription.");
                Assert.That(gunPower.Powered && helmPower.Powered, Is.True, "Weapon setup must preserve bridge power.");
                foreach (var entity in new[] { actor, helm, seat, gun, serverUid, weapon, flare })
                    Assert.That(em.GetComponent<TransformComponent>(entity).GridUid, Is.EqualTo(map.Grid.Owner), $"Fixture {entity} must remain on the connected deck.");
                Assert.That(em.GetComponent<TransformComponent>(gun).Anchored, Is.True);
                Assert.That(em.GetComponent<TransformComponent>(helm).Anchored, Is.True);
                Assert.That(em.GetComponent<TransformComponent>(weapon).Anchored, Is.True);
                Assert.That(em.System<WFCombatConsoleSystem>().TryGetServer(gun, control, out _, out _), Is.True,
                    "Weapon setup must preserve native gunnery server ownership.");
                Assert.That(cockpit.CanOperate(actor, gun), Is.True, "The original cockpit link must remain authorized after mounting weapons.");
            });
            var netGun = em.GetNetEntity(gun);
            var selection = new List<NetEntity> { em.GetNetEntity(weapon), em.GetNetEntity(foreign), em.GetNetEntity(flare), em.GetNetEntity(weapon) };
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key,
                new WFCockpitGunneryCommandMessage(netGun, new WFSaveWeaponGroupMessage(0, selection)) { Actor = actor });
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(gun).Groups[0], Is.EquivalentTo(new[] { weapon }),
                "Cockpit group saves use the native filter for disconnected weapons, flares and duplicates.");
            Assert.That(cockpit.TryCommand(actor, helm, netGun, new WFAutomaticFlaresMessage(true)), Is.True);
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(gun).Automatic, Is.True);
            Assert.That(cockpit.TryCommand(actor, helm, netGun, new WFDispenseFlaresMessage()), Is.True);
            // Fire control schedules bursts; the native gun update performs the actual shot.
            em.System<GunSystem>().Update(1f / 30f);
            Assert.That(em.GetComponent<WFFlareLauncherComponent>(flare).NextBurst, Is.GreaterThan(TimeSpan.Zero));
            var target = em.GetNetCoordinates(new EntityCoordinates(map.Grid.Owner, new Vector2(20, 0)));
            var beforeShot = new GetAmmoCountEvent();
            em.EventBus.RaiseLocalEvent(weapon, ref beforeShot);
            Assert.That(beforeShot.Count, Is.GreaterThan(0), "The real ship autocannon has a finite, loaded magazine.");
            var mountedGun = em.GetComponent<GunComponent>(weapon);
            Assert.That(em.System<GunSystem>().CanShoot(mountedGun), Is.True, "The autocannon must be ready before its first command.");
            Assert.That(mountedGun.FireRateModified, Is.GreaterThan(0));
            var beforeHover = em.System<FireControlSystem>().WfCockpitState(gun, false);
            Assert.That(cockpit.TryCommand(actor, helm, netGun, new FireControlConsoleFireMessage(new(), target)), Is.True);
            Assert.That(em.System<FireControlSystem>().WfCockpitState(gun, false), Is.SameAs(beforeHover),
                "Cursor guidance must not rebuild radar/ammunition state.");
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key,
                new WFCockpitGunneryCommandMessage(netGun, new FireControlConsoleFireMessage(selection, target)) { Actor = actor });
            Assert.That(fireControl.NextFire, Is.GreaterThan(TimeSpan.Zero), "Firing reaches the original weapon cooldown path.");
            Assert.That(em.GetComponent<AutoShootGunComponent>(weapon).RemainingTime, Is.GreaterThan(TimeSpan.Zero),
                "The native firing handler schedules the autocannon burst.");
            em.System<GunSystem>().Update(1f / 30f);
            var afterShot = new GetAmmoCountEvent();
            em.EventBus.RaiseLocalEvent(weapon, ref afterShot);
            Assert.That(afterShot.Count, Is.LessThan(beforeShot.Count), "The original firing path must actually consume ammunition.");
            Assert.That(foreignControl.NextFire, Is.EqualTo(TimeSpan.Zero), "A forged weapon outside the connected server must never fire.");
            Assert.That(control.NextLog, Is.Not.Null, "The native player-attributed firing log must remain active.");
            Assert.That(cockpit.TryCommand(actor, helm, netGun, new WFCockpitGunnerySessionMessage(false)), Is.False,
                "The tunnel only accepts its explicit native command allowlist.");
            Assert.That(cockpit.TryCommand(actor, helm, netGun,
                new FireControlConsoleFireMessage(new(), new NetCoordinates(target.NetEntity, float.NaN, 0))), Is.False);

            Assert.That(gunAccess.AccessLog.Count, Is.EqualTo(1), "Firing and other commands must not flood the access history either.");
            Assert.That(helmAccess.AccessLog.Count, Is.EqualTo(1));

            void AssertDenied(string reason) => Assert.That(cockpit.TryCommand(actor, helm, netGun,
                new WFSaveWeaponGroupMessage(1, new() { em.GetNetEntity(weapon) })), Is.False, reason);
            gunPower.Powered = false;
            AssertDenied("Loss of gunnery power is enforced before the next telemetry tick.");
            gunPower.Powered = true;
            helmPower.Powered = false;
            AssertDenied("Loss of helm power must also revoke commands immediately.");
            helmPower.Powered = true;
            gunAccess.AccessLists.Add(new HashSet<ProtoId<AccessLevelPrototype>> { "Captain" });
            AssertDenied("Revoked gunnery access is enforced on every command.");
            gunAccess.Enabled = false;
            helmAccess.AccessLists.Add(new HashSet<ProtoId<AccessLevelPrototype>> { "Captain" });
            AssertDenied("Revoked helm access is enforced on every command.");
            helmAccess.Enabled = false;
            var gunTransform = em.GetComponent<TransformComponent>(gun);
            var gunPosition = gunTransform.Coordinates;
            transforms.SetCoordinates(gun, gunTransform, new EntityCoordinates(map.Grid.Owner, new Vector2(10, 0)), unanchor: false);
            AssertDenied("An anchored console outside physical reach is not a remote gunnery link.");
            transforms.SetCoordinates(gun, gunTransform, gunPosition, unanchor: false);
            var helmTransform = em.GetComponent<TransformComponent>(helm);
            var helmPosition = helmTransform.Coordinates;
            transforms.SetCoordinates(helm, helmTransform, new EntityCoordinates(map.Grid.Owner, new Vector2(10, 0)), unanchor: false);
            AssertDenied("Moving the helm out of physical reach also revokes the link immediately.");
            transforms.SetCoordinates(helm, helmTransform, helmPosition, unanchor: false);
            server.Consoles.Remove(gun);
            AssertDenied("The claimed gunnery server must still own this console.");
            server.Consoles.Add(gun);
            Assert.That(cockpit.CanOperate(actor, gun), Is.True);

            var otherActor = em.SpawnEntity("MobHuman", map.GridCoords);
            var otherSeat = em.SpawnEntity("ChairPilotSeat", map.GridCoords);
            em.EnsureComponent<PilotComponent>(otherActor);
            helms.AddPilot(helm, otherActor, em.GetComponent<ShuttleConsoleComponent>(helm));
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, otherActor);
            Assert.That(buckles.TryBuckle(otherActor, otherActor, otherSeat), Is.True);
            em.GetComponent<ActivatableUIComponent>(gun).SingleUser = true;
            Assert.That(cockpit.SetSession(otherActor, helm, true, true), Is.False,
                "A seated body without an attached connected player cannot retain gunnery control.");
            var activation = em.GetComponent<ActivatableUIComponent>(gun);
            activation.CurrentSingleUser = otherActor;
            AssertDenied("The native single-user console occupant must block cockpit commands.");
            activation.CurrentSingleUser = null;
            Assert.That(cockpit.CanOperate(actor, gun), Is.True);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, otherActor);
            Assert.That(cockpit.GetConsole(actor), Is.Null, "Leaving the body immediately releases the cockpit link.");
            Assert.That(cockpit.GetActors(gun), Is.Empty, "Ghosting or changing bodies must release the NPC gunner claim.");
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            Assert.That(cockpit.GetConsole(actor), Is.Null, "Returning to the body must not reactivate a stale session.");
            ui.CloseUi(helm, ShuttleConsoleUiKey.Key, otherActor);
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
            Assert.That(cockpit.SetSession(actor, helm, true, true), Is.True);

            var replacement = em.SpawnEntity("ComputerGunneryConsole", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(replacement).Powered = true;
            em.RemoveComponent<AccessReaderComponent>(replacement);
            em.GetComponent<FireControlConsoleComponent>(replacement).ConnectedServer = serverUid;
            server.Consoles.Add(replacement);
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun), "A valid link remains stable when another console appears.");
            gunPower.Powered = false;
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(replacement));
            AssertDenied("A queued command addressed to the old console cannot operate its replacement.");
            // The fixture runs in one tick; simulate seat loss without the normal unbuckling cooldown.
            buckles.Unbuckle((actor, em.GetComponent<BuckleComponent>(actor)), actor);
            Assert.That(em.GetComponent<BuckleComponent>(actor).BuckledTo, Is.Null);
            Assert.That(cockpit.TryCommand(actor, helm, em.GetNetEntity(replacement), new WFDispenseFlaresMessage()), Is.False);
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.Null);
            Assert.That(cockpit.GetActors(replacement), Is.Empty, "Losing the pilot seat releases crew and telemetry occupancy.");
            ui.CloseUi(helm, ShuttleConsoleUiKey.Key, actor);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            foreach (var entity in new[] { actor, otherActor, helm, seat, otherSeat, gun, replacement, flare, weapon, foreign, serverUid })
                em.DeleteEntity(entity);
        });
        await pair.CleanReturnAsync();
    }
}
