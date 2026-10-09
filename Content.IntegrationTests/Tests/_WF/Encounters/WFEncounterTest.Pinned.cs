#nullable enable
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Shared._WF.Encounters;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    /// <summary>
    /// An encounter an admin started has no clock, takes no slot under the cap, and keeps its ships after it resolves,
    /// however far away everyone is, until an admin ends it.
    /// </summary>
    [Test]
    public async Task AdminEncountersStayUntilEnded()
    {
        EntityUid encounter = default, ship = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestStoryRival");
            var admin = SEntMan.GetEntity(Player);
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-41000, -41000), MapData.MapId), out encounter, admin), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            ship = comp.Ships["rival"].Grid;
            Assert.That(comp.Pinned, Is.True, "An admin's encounter is pinned.");
            Assert.That(comp.Expires, Is.Null, "With no clock on it.");
            Assert.That(comp.OffBudget, Is.True, "And no slot under the cap.");

            Server.System<WFEncounterSystem>().Resolve(encounter, WFEncounterResolution.Completed);
            Assert.That(comp.JumpAt, Is.Null, "Resolved, its ships do not jump out.");
        });
        // Well past the jump delay and several cleanup polls, with the player nowhere near.
        await RunTicks(900);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(encounter), Is.True, "The encounter is still there.");
            Assert.That(SEntMan.EntityExists(ship), Is.True, "And so is its ship.");

            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(ship), Is.False, "Ending it removes the ship.");
        });
    }
}
