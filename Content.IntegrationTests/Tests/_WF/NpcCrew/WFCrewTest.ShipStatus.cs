#nullable enable
using System.Numerics;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A ship is disabled once it loses its thrust, or everyone who was aboard, for 10 s; a bare hull never is.</summary>
    [Test]
    public async Task ShipIsDisabledOnceItLosesThrustAndWeapons()
    {
        var wreck = await CreateDeck(new Vector2(6, 0), 3, gravity: true);
        var ship = await CreateDeck(new Vector2(20, 0), 3, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var status = Server.System<WFCrewShipStatusSystem>();
            SEntMan.EnsureComponent<ShuttleComponent>(wreck);
            SEntMan.EnsureComponent<ShuttleComponent>(ship).LinearThrust[0] = 10f;
            Assert.That(status.IsDisabled(wreck), Is.False, "A hull never seen working is not judged.");
            Assert.That(status.IsDisabled(ship), Is.False);
            SEntMan.GetComponent<ShuttleComponent>(ship).LinearThrust[0] = 0f;
        });
        await WaitUntil(() => Server.System<WFCrewShipStatusSystem>().IsDisabled(ship), 2400, () => "ship never counted as disabled");
        var crewed = await CreateDeck(new Vector2(40, 0), 3, gravity: true);
        EntityUid hand = default;
        await Server.WaitAssertion(() =>
        {
            var status = Server.System<WFCrewShipStatusSystem>();
            Assert.That(status.IsDisabled(wreck), Is.False);
            SEntMan.EnsureComponent<ShuttleComponent>(crewed).LinearThrust[0] = 10f;
            hand = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Deckhand,
                new EntityCoordinates(crewed, new Vector2(1.5f)), "status")!.Value;
            SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(hand).Enabled = false;
            Assert.That(status.IsDisabled(crewed), Is.False);
            SEntMan.DeleteEntity(hand);
        });
        await WaitUntil(() => Server.System<WFCrewShipStatusSystem>().IsDisabled(crewed), 2400,
            () => "a ship that lost its whole crew never counted as disabled");
    }
}
