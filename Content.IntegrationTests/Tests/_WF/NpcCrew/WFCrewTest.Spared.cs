#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._Mono.Company;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A crew never turns on a player ship of its own faction, however much it fires.</summary>
    [Test]
    public async Task SameFactionPlayerShipsAreNeverAttackers()
    {
        var patrol = await CreateDeck(new Vector2(1020, 820), 7, true);
        var pdv = await CreateDeck(new Vector2(1020, 880), 7, true);
        var tsf = await CreateDeck(new Vector2(1140, 880), 7, true);
        var npcPdv = await CreateDeck(new Vector2(1140, 820), 7, true);
        await Server.WaitAssertion(() =>
        {
            void Flag(EntityUid grid, string company) =>
                SEntMan.EnsureComponent<CompanyComponent>(grid).CompanyName = company;

            var setup = Server.System<WFCrewSetupSystem>();
            var alerts = Server.System<WFCrewAlertSystem>();
            var spared = Server.System<WFCrewSparedSystem>();
            Flag(patrol, "PDV");
            Flag(pdv, "PDVCivilian");
            Flag(tsf, "TSF");
            Flag(npcPdv, "PDV");
            setup.MarkProfile(patrol, "WFCrewProfileHauler");
            setup.MarkProfile(npcPdv, "WFCrewProfileHauler");
            ConvoyCrew(patrol, "WFCrewGunner", "patrol");

            Assert.That(spared.Spared(patrol, pdv), Is.True, "A PDV crew spares a PDV player ship.");
            Assert.That(spared.Spared(patrol, tsf), Is.False);
            Assert.That(spared.Spared(patrol, npcPdv), Is.False, "NPC crews settle their own fights.");

            for (var i = 0; i < 60; i++)
            {
                Assert.That(alerts.ReportIncomingFire(patrol, "patrol", pdv), Is.False);
            }

            alerts.ReportShipThreat(patrol, "patrol", pdv);
            Assert.That(alerts.IsAttacker(patrol, pdv), Is.False);
            Assert.That(alerts.IsHostileShip(patrol, "patrol", pdv), Is.False);

            alerts.ReportShipThreat(patrol, "patrol", tsf);
            Assert.That(alerts.IsHostileShip(patrol, "patrol", tsf), Is.True, "Other ships can still be fought.");
        });
    }
}
