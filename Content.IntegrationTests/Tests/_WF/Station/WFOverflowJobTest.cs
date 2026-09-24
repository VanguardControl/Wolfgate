using System.Collections.Generic;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Station;

/// <summary>Round-start overflow only hands out Vagrant, never another unlimited job.</summary>
[TestFixture]
[TestOf(typeof(StationJobsSystem))]
public sealed class WFOverflowJobTest
{
    private const string WithVagrantMap = "WFTestOverflowWithVagrant";
    private const string WithoutVagrantMap = "WFTestOverflowWithoutVagrant";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: playTimeTracker
  id: WFTestOverflowWantedTracker

- type: playTimeTracker
  id: WFTestOverflowMarineTracker

- type: playTimeTracker
  id: WFTestOverflowCaptainTracker

- type: job
  id: WFTestOverflowWanted
  playTimeTracker: WFTestOverflowWantedTracker

- type: job
  id: WFTestOverflowMarine
  playTimeTracker: WFTestOverflowMarineTracker

- type: job
  id: WFTestOverflowCaptain
  playTimeTracker: WFTestOverflowCaptainTracker

- type: gameMap
  id: {WithVagrantMap}
  minPlayers: 0
  mapName: {WithVagrantMap}
  mapPath: /Maps/Test/empty.yml
  stations:
    Station:
      mapNameTemplate: {WithVagrantMap}
      stationProto: StandardNanotrasenStation
      components:
        - type: StationJobs
          availableJobs:
            Contractor: [-1, -1]
            WFTestOverflowMarine: [-1, -1]
            WFTestOverflowCaptain: [1, 1]

- type: gameMap
  id: {WithoutVagrantMap}
  minPlayers: 0
  mapName: {WithoutVagrantMap}
  mapPath: /Maps/Test/empty.yml
  stations:
    Station:
      mapNameTemplate: {WithoutVagrantMap}
      stationProto: StandardNanotrasenStation
      components:
        - type: StationJobs
          availableJobs:
            WFTestOverflowMarine: [-1, -1]
            WFTestOverflowCaptain: [1, 1]
";

    [Test]
    public async Task OverflowIsVagrantOnlyTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var stationJobs = entMan.System<StationJobsSystem>();
        var stationSystem = entMan.System<StationSystem>();

        var withVagrant = EntityUid.Invalid;
        var withoutVagrant = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            withVagrant = stationSystem.InitializeNewStation(
                protoMan.Index<GameMapPrototype>(WithVagrantMap).Stations["Station"], null, WithVagrantMap);
            withoutVagrant = stationSystem.InitializeNewStation(
                protoMan.Index<GameMapPrototype>(WithoutVagrantMap).Stations["Station"], null, WithoutVagrantMap);
        });

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!.UserId;
            // Only a job no station offers, as when Emergency Responder is set on a Hyperwar round.
            var profile = new HumanoidCharacterProfile()
                .WithJobPriority(SharedGameTicker.FallbackOverflowJob, JobPriority.Never)
                .WithJobPriority("WFTestOverflowWanted", JobPriority.High)
                .WithPreferenceUnavailable(PreferenceUnavailableMode.SpawnAsOverflow);
            var profiles = new Dictionary<NetUserId, HumanoidCharacterProfile> { [player] = profile };

            Assert.That(stationJobs.GetOverflowJobs(withVagrant),
                Is.EquivalentTo(new[] { new ProtoId<JobPrototype>(SharedGameTicker.FallbackOverflowJob) }));
            Assert.That(stationJobs.GetOverflowJobs(withoutVagrant), Is.Empty);

            // No Vagrant slot anywhere: the player stays in the lobby instead of getting a random job.
            var assigned = stationJobs.AssignJobs(new Dictionary<NetUserId, HumanoidCharacterProfile>(profiles), new[] { withoutVagrant });
            stationJobs.AssignOverflowJobs(ref assigned, new[] { player }, profiles, new[] { withoutVagrant });
            Assert.That(assigned, Does.Not.ContainKey(player));
            Assert.That(stationJobs.PickBestAvailableJobWithPriority(withoutVagrant, profile.JobPriorities, true, new HashSet<ProtoId<JobPrototype>>()),
                Is.Null);

            // A Vagrant slot exists: that is the only overflow they can get.
            for (var i = 0; i < 20; i++)
            {
                assigned = stationJobs.AssignJobs(new Dictionary<NetUserId, HumanoidCharacterProfile>(profiles), new[] { withVagrant, withoutVagrant });
                stationJobs.AssignOverflowJobs(ref assigned, new[] { player }, profiles, new[] { withVagrant, withoutVagrant });
                Assert.That(assigned[player], Is.EqualTo(((ProtoId<JobPrototype>?) SharedGameTicker.FallbackOverflowJob, withVagrant)));
            }
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(withVagrant);
            entMan.DeleteEntity(withoutVagrant);
        });

        await pair.CleanReturnAsync();
    }
}
