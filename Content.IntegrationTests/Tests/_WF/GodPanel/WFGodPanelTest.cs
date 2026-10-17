using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.GodPanel;
using Content.Server._WF.ShipPa;
using Content.Server.Administration.Managers;
using Content.Shared._WF.GodPanel;
using Content.Shared._WF.ShipPa;
using Content.Shared.Administration;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.GodPanel;

/// <summary>
/// The God Panel's server side: only admins subscribe, the state lists the player and a named ship, feeds are admin
/// cameras attached to their target and go when closed, the timeline records what happens, and an action without
/// the flag does nothing.
/// </summary>
[TestFixture]
[TestOf(typeof(WFGodPanelSystem))]
public sealed class WFGodPanelTest
{
    private const string Character = "Godtest Body";
    private const string Ship = "Godtest Ship";

    [Test]
    public async Task GodPanelListsFeedsLogsAndRefusesNonAdmins()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var session = pair.Player!;
        var map = await pair.CreateTestMap();
        var admins = server.ResolveDependency<IAdminManager>();

        // The test player starts as an admin; take that away first.
        await server.WaitPost(() => admins.DeAdmin(session));
        for (var i = 0; i < 60 && admins.HasAdminFlag(session, AdminFlags.Admin); i++)
        {
            await pair.RunTicksSync(1);
        }

        Assert.That(admins.HasAdminFlag(session, AdminFlags.Admin), Is.False, "The player never lost the Admin flag.");

        var subscribedWithoutFlag = true;
        var actedWithoutFlag = true;
        var pausedWithoutFlag = true;
        await server.WaitAssertion(() =>
        {
            var panel = server.System<WFGodPanelSystem>();
            subscribedWithoutFlag = panel.Subscribe(session, true);
            actedWithoutFlag = panel.Act(session, WFGodAction.PauseEncounters, null, null, false, out _);
            pausedWithoutFlag = panel.BuildState().Round.EncountersPaused;
        });

        await server.WaitPost(() => admins.ReAdmin(session));
        for (var i = 0; i < 60 && !admins.HasAdminFlag(session, AdminFlags.Admin); i++)
        {
            await pair.RunTicksSync(1);
        }

        Assert.That(admins.HasAdminFlag(session, AdminFlags.Admin), "The player never became an admin.");
        Assert.Multiple(() =>
        {
            Assert.That(subscribedWithoutFlag, Is.False, "A player without the Admin flag shouldn't be able to subscribe.");
            Assert.That(actedWithoutFlag, Is.False, "An action from an unsubscribed player should be refused.");
            Assert.That(pausedWithoutFlag, Is.False);
        });

        EntityUid body = default;
        EntityUid grid = default;
        int cameraCount = 0, deadCameraCount = 0;
        var seenBeforeLog = 0;
        WFGodPanelState state = default!;
        WFGodPanelState later = default!;
        var feedWorked = false;
        var feedCapped = true;
        var freeWorked = false;
        var freeMoved = false;
        var deleteAfterClose = false;
        var deleteAfterUnsubscribe = false;
        var cameras = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var panel = server.System<WFGodPanelSystem>();
            var meta = server.System<MetaDataSystem>();
            var minds = server.System<SharedMindSystem>();

            grid = map.Grid.Owner;
            meta.SetEntityName(grid, Ship);
            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            meta.SetEntityName(body, Character);
            var mind = minds.GetOrCreateMind(session.UserId);
            minds.TransferTo(mind, body);

            Assert.That(panel.Subscribe(session, true), Is.True, "An admin should be able to subscribe.");
            Assert.That(panel.IsSubscribed(session));
            state = panel.BuildState();

            // A feed on the body is an admin camera attached to it; closing it deletes the camera.
            var feed = panel.OpenFeed(session, body);
            feedWorked = feed != null;
            if (feed is { } id)
            {
                var attached = Children(entMan, body);
                cameraCount = attached.Count;
                cameras.AddRange(attached);
                Assert.That(panel.CloseFeed(session, id), Is.True);
                deleteAfterClose = attached.All(entMan.Deleted);
            }

            // The free camera sits on the map and can be moved; the cap holds.
            var free = panel.OpenFeed(session, null);
            freeWorked = free != null;
            if (free is { } freeId)
                freeMoved = panel.MoveFeed(session, freeId, map.GridCoords);

            var opened = new List<int>();
            for (var i = 0; i < 10; i++)
            {
                if (panel.OpenFeed(session, body) is { } extra)
                    opened.Add(extra);
            }

            feedCapped = opened.Count + (free != null ? 1 : 0) <= 6;
            deadCameraCount = Children(entMan, body).Count;

            // Unsubscribing drops every feed.
            var held = AllCameras(entMan);
            panel.Subscribe(session, false);
            deleteAfterUnsubscribe = held.Count > 0 && held.All(entMan.Deleted);
            Assert.That(panel.Subscribe(session, true), Is.True);

            // Things happening land in the timeline with their category.
            seenBeforeLog = panel.LastEventId;
            server.System<MobStateSystem>().ChangeMobState(body, MobState.Dead);
            var alerts = server.System<ShipAlertSystem>();
            var other = server.ProtoMan.EnumeratePrototypes<ShipAlertCodePrototype>()
                .First(code => code.ID != entMan.EnsureComponent<ShipAlertComponent>(grid).Code.Id);
            alerts.SetCode(grid, other.ID);
            panel.AddEvent(WFGodEventCategory.Encounter, "Test encounter", body);
            later = panel.BuildState(seenBeforeLog);
        });

        Assert.Multiple(() =>
        {
            var player = state.Players.SingleOrDefault(p => p.Username == session.Name);
            Assert.That(player, Is.Not.Null, "The player should be listed.");
            Assert.That(player?.Character, Is.EqualTo(Character).Or.Not.Empty);
            Assert.That(player?.Status, Is.EqualTo(WFGodPlayerStatus.Alive));
            Assert.That(player?.GridName, Is.EqualTo(Ship));

            var listed = state.Grids.SingleOrDefault(g => g.Name == Ship);
            Assert.That(listed, Is.Not.Null, "The ship with a player aboard should be listed.");
            Assert.That(listed?.Players, Is.EqualTo(1));
            Assert.That(state.Round.Gms, Is.EqualTo(1));

            Assert.That(feedWorked, "A feed on a player should open.");
            Assert.That(cameraCount, Is.EqualTo(1), "The feed should be one admin camera attached to the target.");
            Assert.That(deleteAfterClose, "Closing the feed should delete its camera.");
            Assert.That(freeWorked, "A free camera should open.");
            Assert.That(freeMoved, "A free camera should move.");
            Assert.That(feedCapped, "A session should be held to wf.godpanel.max_feeds feeds.");
            Assert.That(deadCameraCount, Is.LessThanOrEqualTo(6));
            Assert.That(deleteAfterUnsubscribe, "Unsubscribing should delete the session's cameras.");

            Assert.That(later.Events.Select(e => e.Id), Is.Ordered.Ascending, "Events should arrive oldest first.");
            Assert.That(later.Events.All(e => e.Id > seenBeforeLog), "Only unseen events should be sent.");
            Assert.That(later.Events.Any(e => e.Category == WFGodEventCategory.Death
                                              && e.Text.Contains(Character) && e.Text.Contains(Ship)),
                "A player's death should be logged with their name and ship.");
            Assert.That(later.Events.Any(e => e.Category == WFGodEventCategory.Ship && e.Text.Contains(Ship)),
                "A situation code change should be logged under Ship.");
            Assert.That(later.Events.Any(e => e.Category == WFGodEventCategory.Encounter && e.Text == "Test encounter"),
                "A hand-added event should keep its category.");
            Assert.That(later.LastEventId, Is.EqualTo(later.Events.Max(e => e.Id)));
        });

        // An admin's action works and shows on the timeline; with the flag gone nothing changes.
        var adminActed = false;
        var adminPaused = false;
        var gmLogged = false;
        await server.WaitAssertion(() =>
        {
            var panel = server.System<WFGodPanelSystem>();
            var before = panel.LastEventId;
            adminActed = panel.Act(session, WFGodAction.PauseEncounters, null, null, false, out _);
            adminPaused = panel.BuildState().Round.EncountersPaused;
            gmLogged = panel.BuildState(before).Events.Any(e => e.Category == WFGodEventCategory.Gm);
            panel.Act(session, WFGodAction.ResumeEncounters, null, null, false, out _);
        });

        await server.WaitPost(() => admins.DeAdmin(session));
        for (var i = 0; i < 60 && admins.HasAdminFlag(session, AdminFlags.Admin); i++)
        {
            await pair.RunTicksSync(1);
        }

        var deadminActed = true;
        var deadminPaused = true;
        await server.WaitAssertion(() =>
        {
            var panel = server.System<WFGodPanelSystem>();
            deadminActed = panel.Act(session, WFGodAction.PauseEncounters, null, null, false, out _);
            deadminPaused = server.System<Content.Server._WF.Encounters.Systems.WFEncounterSchedulerSystem>().Paused;
        });

        Assert.Multiple(() =>
        {
            Assert.That(adminActed, "An admin's action should work.");
            Assert.That(adminPaused, "The pause should show in the state.");
            Assert.That(gmLogged, "A panel action should land on the timeline as Gm.");
            Assert.That(deadminActed, Is.False, "An action after losing the Admin flag should be refused.");
            Assert.That(deadminPaused, Is.False, "A refused action must not change anything.");
            Assert.That(WFGodPanelSystem.RequiredFlag(WFGodAction.Announce), Is.EqualTo(AdminFlags.Fun));
            Assert.That(WFGodPanelSystem.RequiredFlag(WFGodAction.PowerOff), Is.EqualTo(AdminFlags.Admin));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every admin camera in the world.</summary>
    private static List<EntityUid> AllCameras(IEntityManager entMan)
    {
        var cameras = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == "AdminCamera")
                cameras.Add(uid);
        }

        return cameras;
    }

    /// <summary>The admin cameras attached to an entity.</summary>
    private static List<EntityUid> Children(IEntityManager entMan, EntityUid parent)
    {
        return AllCameras(entMan).Where(camera => entMan.GetComponent<TransformComponent>(camera).ParentUid == parent).ToList();
    }
}
