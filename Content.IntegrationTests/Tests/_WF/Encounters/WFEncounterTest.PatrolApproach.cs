#nullable enable
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Encounters;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>A patrol that closes on a vessel itself does not take it for a trespasser.</summary>
public sealed partial class WFEncounterTest
{
    /// <summary>An enemy the patrol moves onto is left alone; once it closes on the patrol itself it is engaged.</summary>
    [Test]
    public async Task PatrolThatClosesInDoesNotTriggerItsZone()
    {
        EntityUid encounter = default, patrol = default, intruder = default, host = default;
        await Server.WaitAssertion(() =>
        {
            host = SEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            SEntMan.AddComponent<Content.Server._NF.SectorServices.StationSectorServiceHostComponent>(host);

            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterWarZone");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(27000, 27000), MapData.MapId), out encounter), Is.True);
            patrol = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["patrol"].Grid;

            var transform = Server.System<SharedTransformSystem>();
            var at = transform.GetWorldPosition(patrol);
            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, at + new Vector2(1000, 0)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
            SEntMan.EnsureComponent<CompanyComponent>(intruder).CompanyName = "RedSailCorsairs";
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            // The patrol comes up on the parked ship, well inside its attack zone.
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(patrol, new EntityCoordinates(MapData.MapUid, transform.GetWorldPosition(intruder) - new Vector2(250, 0)));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["patrol"];
            Assert.That(state.Engaged, Is.Empty, "A ship the patrol came to is not fired on.");
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(patrol, state.Group), Is.Empty);

            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, transform.GetWorldPosition(intruder) - new Vector2(100, 0)));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["patrol"];
            Assert.That(state.Engaged.Contains(intruder), Is.True, "Closing on the patrol itself, it is an intruder.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
            SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }
}
