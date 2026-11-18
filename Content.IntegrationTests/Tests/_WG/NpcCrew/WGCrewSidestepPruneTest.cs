#nullable enable
using System.Collections.Generic;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

/// <summary>
/// Stuck crew's sidestep waypoints are pruned once the table grows past 64: a crewman still heading for his waypoint
/// keeps it, while waypoints of crewmen who are gone or have moved on to another path are dropped.
/// </summary>
[TestFixture]
[TestOf(typeof(NPCSteeringSystem))]
public sealed class WGCrewSidestepPruneTest
{
    [Test]
    public async Task PruneKeepsActiveWaypointsAndDropsStaleOnes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var steering = entMan.System<NPCSteeringSystem>();
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            PathPoly Waypoint(int i) => new(map.Grid.Owner, Vector2i.Zero, (byte) i, Box2.UnitCentered, default);

            EntityUid Walker()
            {
                var uid = entMan.SpawnEntity(null, new MapCoordinates(0, 0, map.MapId));
                entMan.AddComponent<NPCSteeringComponent>(uid);
                return uid;
            }

            // Ten crewmen still heading for their waypoints.
            var active = new List<EntityUid>();
            for (var i = 0; i < 10; i++)
            {
                var uid = Walker();
                var waypoint = Waypoint(i);
                entMan.GetComponent<NPCSteeringComponent>(uid).CurrentPath.Enqueue(waypoint);
                steering.TrackCrewSidestep(uid, waypoint);
                active.Add(uid);
            }

            // Fifty who moved on to another path or have none left, and five who are gone.
            var stale = new List<EntityUid>();
            for (var i = 0; i < 50; i++)
            {
                var uid = Walker();
                if (i % 2 == 0)
                    entMan.GetComponent<NPCSteeringComponent>(uid).CurrentPath.Enqueue(Waypoint(100 + i));
                steering.TrackCrewSidestep(uid, Waypoint(i));
                stale.Add(uid);
            }

            for (var i = 0; i < 5; i++)
            {
                var uid = Walker();
                steering.TrackCrewSidestep(uid, Waypoint(i));
                entMan.DeleteEntity(uid);
                stale.Add(uid);
            }

            // (A pooled server may hold entries from earlier tests: the checks look only at this test's own.)
            Assert.That(steering.CrewSidestepCount, Is.GreaterThan(64), "Precondition: past the 64 that start a prune.");
            steering.PruneCrewUnsticks(now);

            using var scope = Assert.EnterMultipleScope();
            foreach (var uid in active)
                Assert.That(steering.HasCrewSidestep(uid), Is.True, "A crewman still heading for his waypoint keeps it.");
            foreach (var uid in stale)
                Assert.That(steering.HasCrewSidestep(uid), Is.False, "A stale waypoint is dropped.");

            // Below the threshold (only live waypoints are left) nothing is pruned: stale entries wait for the table to
            // grow or their owner's next look.
            Assert.That(steering.CrewSidestepCount, Is.LessThanOrEqualTo(64));
            var lone = Walker();
            steering.TrackCrewSidestep(lone, Waypoint(200));
            steering.PruneCrewUnsticks(now);
            Assert.That(steering.HasCrewSidestep(lone), Is.True);

            // Leave the table as it was found.
            foreach (var uid in active)
                steering.ForgetCrewSidestep(uid);
            steering.ForgetCrewSidestep(lone);
        });

        await pair.CleanReturnAsync();
    }
}
