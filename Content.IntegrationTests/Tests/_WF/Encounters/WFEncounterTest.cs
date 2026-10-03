#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>
/// Encounters: a prototype's ships spawn crewed, in their sides, with their rules and orders; an encounter resolves
/// when no ship is left and ending one removes its ships and crews.
/// </summary>
[TestOf(typeof(WFEncounterSystem))]
public sealed class WFEncounterTest : InteractionTest
{
    private const string Convoy = "WFTestEncounterConvoy";

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfEncounter
  id: WFTestEncounterConvoy
  name: wf-encounter-name-convoy
  scheduled: false
  ships:
  - key: lead
    vessel: WFDredger
    side: convoy
    disengage: Deter
    disengageRange: 800
  - key: escort
    vessel: WFDredger
    offset: 200, 0
    side: convoy
    captain: false
    objectives:
    - kind: Escort
      target: lead
";

    [Test]
    public async Task EncounterSpawnsCrewedShipsAndEndsCleanly()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WFEncounterSystem>();
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(Convoy);
            Assert.That(system.TrySpawn(prototype, new MapCoordinates(new Vector2(600, 600), MapData.MapId), out encounter), Is.True);
            Assert.That(system.ActiveCount(), Is.EqualTo(1));
        });
        await RunTicks(5);

        EntityUid lead = default, escort = default;
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            lead = comp.Ships["lead"].Grid;
            escort = comp.Ships["escort"].Grid;
            Assert.That(SEntMan.GetComponent<WFEncounterGridComponent>(lead).Encounter, Is.EqualTo(encounter));

            var crews = Server.System<WFCrewObjectiveSystem>().Snapshot().ToDictionary(crew => SEntMan.GetEntity(crew.Grid));
            Assert.That(crews[lead].Alive, Is.GreaterThanOrEqualTo(2));
            Assert.That(crews[lead].Settings.Disengage, Is.EqualTo(WFCrewDisengage.Deter));
            Assert.That(crews[lead].Settings.DisengageRange, Is.EqualTo(800f));
            Assert.That(crews[escort].Objectives.Single().Kind, Is.EqualTo(WFCrewObjectiveKind.Escort));
            Assert.That(crews[escort].Objectives.Single().Target, Is.EqualTo(SEntMan.GetNetEntity(lead)));
            Assert.That(crews[lead].Settings.Battlegroup, Is.Not.Empty);
            Assert.That(crews[lead].Settings.Battlegroup, Is.EqualTo(crews[escort].Settings.Battlegroup));
            Assert.That(Server.System<WFCrewEscortSystem>().AreInFormation(lead, escort), Is.True);

            var system = Server.System<WFEncounterSystem>();
            system.Resolve(encounter, WFEncounterResolution.Expired);
            Assert.That(comp.Resolution, Is.EqualTo(WFEncounterResolution.Expired));
            Assert.That(system.ActiveCount(), Is.Zero, "A resolved encounter no longer counts against the cap.");
            Assert.That(SEntMan.Deleted(lead), Is.False, "Resolved ships stay until players have left them.");
            system.End(encounter);
        });
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(lead) && SEntMan.Deleted(escort) && SEntMan.Deleted(encounter), Is.True);
            Assert.That(SEntMan.EntityQuery<WFCrewComponent>().Any(), Is.False, "Ending an encounter removes its crews.");
        });
    }
}
