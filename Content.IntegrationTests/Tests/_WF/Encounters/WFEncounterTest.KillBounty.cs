#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.KillTracking;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    /// <summary>
    /// Killing a Reaver crewman pays the killer the company's kill bounty at once, the assist half of it, and it still
    /// pays once the encounter is over, as for boarders left aboard a player's ship.
    /// </summary>
    [Test]
    public async Task KillingAReaverPaysOnTheSpot()
    {
        var config = Server.ResolveDependency<IConfigurationManager>();
        var cap = 0;
        var bounty = 0;
        EntityUid encounter = default;
        await Server.WaitPost(() =>
        {
            cap = config.GetCVar(EncountersCVars.PayoutHourlyCap);
            // Other tests on this pair may have used the hour's allowance.
            config.SetCVar(EncountersCVars.PayoutHourlyCap, int.MaxValue);
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            bounty = prototypes.Index<WFStandingPrototype>("WFAshfallReavers").KillBounty;
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestSimRaider"),
                new MapCoordinates(new Vector2(-52000, 52000), MapData.MapId), out encounter), Is.True);
        });
        Assert.That(bounty, Is.GreaterThan(0), "The Reavers carry a kill bounty.");
        await Until(() => !Ship(encounter).Virtual && !Ship(encounter).Building, 30, "The raider's hull is built.");

        await Server.WaitAssertion(() =>
        {
            var group = Ship(encounter).Group;
            var crew = SEntMan.EntityQuery<WFCrewComponent>(true).Where(member => member.Group == group).Select(member => member.Owner).ToList();
            Assert.That(crew, Has.Count.GreaterThanOrEqualTo(2));

            var player = SEntMan.GetEntity(Player);
            SEntMan.EnsureComponent<BankAccountComponent>(player);
            var bank = Server.System<BankSystem>();
            bank.TryGetBalance(player, out var before);

            var kill = new KillReportedEvent(crew[0], new KillPlayerSource(ServerSession.UserId), null, false);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref kill);
            bank.TryGetBalance(player, out var afterKill);
            Assert.That(afterKill - before, Is.EqualTo(bounty), "The killer is paid the bounty at once.");

            // An assist by the killer themself is no second share.
            Server.System<WFEncounterSystem>().Resolve(encounter, WFEncounterResolution.Ended);
            var self = new KillReportedEvent(crew[1], new KillPlayerSource(ServerSession.UserId), new KillPlayerSource(ServerSession.UserId), false);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref self);
            bank.TryGetBalance(player, out var afterResolved);
            Assert.That(afterResolved - afterKill, Is.EqualTo(bounty), "Once the encounter is over its crew still pay, and only once each.");

            var npc = new KillReportedEvent(crew[0], new KillNpcSource(crew[1]), new KillPlayerSource(ServerSession.UserId), false);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref npc);
            bank.TryGetBalance(player, out var afterAssist);
            Assert.That(afterAssist - afterResolved, Is.EqualTo(bounty / 2), "An assist is paid half.");

            Server.System<WFEncounterSystem>().End(encounter);
            config.SetCVar(EncountersCVars.PayoutHourlyCap, cap);
        });
        await RunTicks(10);
    }
}
