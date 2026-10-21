#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._Mono.Company;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>
    /// A crew goes by the faction of the players aboard, not the hull's flag, and answers its own faction's player
    /// vessel only once that vessel has landed a great many hits.
    /// </summary>
    [Test]
    public async Task OwnFactionPlayerShipsNeedMassiveFriendlyFire()
    {
        var patrol = await CreateDeck(new Vector2(1020, 820), 7, true);
        var player = await CreateDeck(new Vector2(1020, 880), 7, true);
        var tsf = await CreateDeck(new Vector2(1140, 880), 7, true);
        var npcPdv = await CreateDeck(new Vector2(1140, 820), 7, true);
        WFCrewAlertSystem alerts = default!;
        await Server.WaitAssertion(() =>
        {
            void Flag(EntityUid uid, string company) =>
                SEntMan.EnsureComponent<CompanyComponent>(uid).CompanyName = company;

            var setup = Server.System<WFCrewSetupSystem>();
            var spared = Server.System<WFCrewSparedSystem>();
            alerts = Server.System<WFCrewAlertSystem>();
            Flag(patrol, "PDV");
            Flag(player, "TSF");
            Flag(tsf, "TSF");
            Flag(npcPdv, "PDV");
            setup.MarkProfile(patrol, "WFCrewProfileHauler");
            setup.MarkProfile(npcPdv, "WFCrewProfileHauler");
            ConvoyCrew(patrol, "WFCrewGunner", "patrol");

            // A PDV player aboard a TSF-flagged hull makes it a PDV vessel.
            Flag(SPlayer, "PDVCivilian");
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(player, new Vector2(0.5f, 0.5f)));
            Assert.That(spared.Spared(patrol, player), Is.True, "The player's faction decides, not the hull's.");
            Assert.That(spared.Spared(patrol, tsf), Is.False, "An empty TSF hull is not PDV.");
            Assert.That(spared.Spared(patrol, npcPdv), Is.False, "NPC crews settle their own fights.");

            alerts.ReportShipThreat(patrol, "patrol", player);
            Assert.That(alerts.IsHostileShip(patrol, "patrol", player), Is.False, "No report turns it on its own faction.");
        });

        // One hit a second for 40 seconds stays inside the allowance of any 30 s window.
        for (var i = 0; i < 25; i++)
        {
            await Server.WaitAssertion(() =>
                Assert.That(alerts.ReportIncomingFire(patrol, "patrol", player), Is.False, $"Hit {i} is excused."));
            await RunTicks((int) STiming.TickRate);
        }

        await Server.WaitAssertion(() => Assert.That(alerts.IsAttacker(patrol, player), Is.False));

        // Sustained fire, two hits a second, runs past it.
        var attacked = false;
        for (var i = 0; i < 60 && !attacked; i++)
        {
            await Server.WaitAssertion(() => attacked = alerts.ReportIncomingFire(patrol, "patrol", player));
            await RunTicks((int) STiming.TickRate / 2 + 1);
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(attacked && alerts.IsAttacker(patrol, player), Is.True, "Massive friendly fire is answered.");
            alerts.ReportShipThreat(patrol, "patrol", tsf);
            Assert.That(alerts.IsHostileShip(patrol, "patrol", tsf), Is.True, "Other ships can still be fought.");
        });
    }
}
