#nullable enable
using System.Numerics;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Shuttles.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A ship is disabled only after it was seen working and then lost both thrust and weapons for 30 s.</summary>
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
        await Server.WaitAssertion(() =>
            Assert.That(Server.System<WFCrewShipStatusSystem>().IsDisabled(wreck), Is.False));
    }
}
