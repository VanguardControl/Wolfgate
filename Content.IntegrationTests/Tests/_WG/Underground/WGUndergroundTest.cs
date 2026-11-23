#nullable enable
using System.Linq;
using System.Collections.Generic;
using Content.Server._Mono.PDV.Components;
using Content.Server._WG.CargoSales;
using Content.Server._WG.Underground;
using Content.Server.Access.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Contraband.Events;
using Content.Shared._WG.Newbie;
using Content.Shared._WG.RoundSetup;
using Content.Shared._WG.Underground;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Contraband;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Shuttles.Components;
using Content.Shared.Turrets;
using Content.Shared.Verbs;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Underground;

/// <summary>The Underground's Heat, warrants, bail and pallet rules, and its Fixer job.</summary>
[TestFixture]
public sealed class WGUndergroundTest
{
    private const string Fixer = "WGUndergroundFixer";

    /// <summary>Heat climbs through the levels, cools when quiet, opens a raid window at the top and can't reopen one straight after.</summary>
    [Test]
    public async Task HeatRisesCoolsAndRaids()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var heat = server.System<WGUndergroundHeatSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var now = timing.CurTime;
            Assert.That(heat.Level, Is.EqualTo(0));
            Assert.That(heat.PdaLine(), Is.Null, "A round with no Underground station shows no Heat line.");

            heat.Add(3);
            Assert.That(heat.Level, Is.EqualTo(1));
            heat.Add(3);
            Assert.That(heat.Level, Is.EqualTo(2));

            // Five quiet minutes, then 0.2 a minute: 14 minutes of cooling takes 2.8 off.
            heat.Tick(now + TimeSpan.FromMinutes(19));
            Assert.That(heat.Level, Is.EqualTo(1));
            Assert.That(heat.Heat, Is.EqualTo(3.2).Within(0.05));

            heat.Add(7);
            Assert.That(heat.Level, Is.EqualTo(WGUndergroundHeatSystem.Raid));
            Assert.That(heat.RaidEnds, Is.Not.Null);
            var peak = heat.Heat;
            heat.Add(5);
            Assert.That(heat.Heat, Is.EqualTo(peak), "Heat holds while a raid window is open.");

            heat.Tick(heat.RaidEnds!.Value);
            Assert.That(heat.RaidEnds, Is.Null);
            Assert.That(heat.Level, Is.EqualTo(1));

            heat.Add(50);
            Assert.That(heat.Level, Is.EqualTo(2), "No second raid window opens straight after the first.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A member's sale adds Heat; a courier's or a marine's sale of the same crate adds none and still pays the treasury.</summary>
    [Test]
    public async Task OnlyMembersRaiseHeat()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var heat = server.System<WGUndergroundHeatSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            var member = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(member);
            var courier = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var marine = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<CompanyComponent>(marine).CompanyName = "TSF";

            void Sell(EntityUid seller)
            {
                var crate = entMan.SpawnEntity("CrateTradeContrabandSecureUnderground", map.GridCoords);
                var sale = new WGPalletSaleEvent(new HashSet<EntityUid> { crate }, map.Grid, Seller: seller);
                entMan.EventBus.RaiseEvent(EventSource.Local, ref sale);
                entMan.DeleteEntity(crate);
            }

            var quiet = heat.Heat;
            var treasury = heat.Treasury;
            var cut = cfg.GetCVar(WGUndergroundCVars.TreasuryJob);

            Sell(courier);
            Sell(marine);
            Assert.That(heat.Heat, Is.EqualTo(quiet), "A sale by someone outside the Underground added Heat.");
            Assert.That(heat.Treasury, Is.EqualTo(treasury + 2 * cut), "A courier's sale didn't pay the treasury.");

            Sell(member);
            Assert.That(heat.Heat, Is.EqualTo(quiet + cfg.GetCVar(WGUndergroundCVars.HeatSupply)).Within(0.01), "A member's sale didn't add its Heat.");

            // Raising the outsider share never lets the factions that gain from a raid run the Heat up.
            cfg.SetCVar(WGUndergroundCVars.HeatOutsider, 0.5f);
            Assert.That(heat.HeatShare(courier), Is.EqualTo(0.5f));
            Assert.That(heat.HeatShare(marine), Is.EqualTo(0f));
            Assert.That(heat.HeatShare(member), Is.EqualTo(1f));
            Assert.That(heat.HeatShare(null), Is.EqualTo(1f), "A sale with no known seller counts as the Underground's.");
            cfg.SetCVar(WGUndergroundCVars.HeatOutsider, 0f);

            entMan.DeleteEntity(member);
            entMan.DeleteEntity(courier);
            entMan.DeleteEntity(marine);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The station jumps to a new place in its band, then not again for an hour, and never while a raid window is open.</summary>
    [Test]
    public async Task StationHopsOnceAnHourAndNotInARaid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var heat = server.System<WGUndergroundHeatSystem>();
        var station = server.System<WGUndergroundBaseSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var home = entMan.AddComponent<WGUndergroundBaseComponent>(grid);
            var from = xforms.GetWorldPosition(grid);

            heat.Add(100);
            Assert.That(heat.RaidEnds, Is.Not.Null);
            Assert.That(station.TryHop((grid, home), out var refusal), Is.False, "The station jumped out of a raid.");
            Assert.That(refusal, Is.EqualTo(Loc.GetString("wg-underground-hop-raid")));
            heat.Tick(heat.RaidEnds!.Value);

            Assert.That(station.TryHop((grid, home), out refusal), Is.True, refusal);
            Assert.That(entMan.TryGetComponent<FTLComponent>(grid, out var ftl), "The station didn't start its jump.");
            var target = xforms.ToMapCoordinates(ftl!.TargetCoordinates).Position;
            Assert.That(target.Length(), Is.InRange(cfg.GetCVar(WGUndergroundCVars.HopMinRange), cfg.GetCVar(WGUndergroundCVars.HopMaxRange)));
            Assert.That((target - from).Length(), Is.GreaterThanOrEqualTo(cfg.GetCVar(WGUndergroundCVars.HopMove)));

            // The hour runs from the jump, whatever became of it.
            Assert.That(server.System<ShuttleSystem>().WgAbortFtlSpool(grid));
            Assert.That(station.TryHop((grid, home), out refusal), Is.False, "The station jumped twice in an hour.");
            Assert.That(refusal, Is.EqualTo(Loc.GetString("wg-underground-hop-wait", ("minutes", 60))));

            home.NextHop = TimeSpan.Zero;
            Assert.That(station.CanHop((grid, home), out _), "The station can't jump once its hour is up.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A member's warrant starts at its base, grows with their jobs and stops at its cap.</summary>
    [Test]
    public async Task WarrantGrowsWithJobs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var warrants = server.System<WGUndergroundWarrantSystem>();

        await server.WaitAssertion(() =>
        {
            var body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var member = entMan.AddComponent<WGUndergroundMemberComponent>(body);
            var contraband = entMan.GetComponent<ContrabandComponent>(body);
            Assert.That(contraband.TurnInValues[WGUndergroundWarrantSystem.Credits], Is.EqualTo(member.BaseWarrant));

            warrants.Worked(body);
            Assert.That(contraband.TurnInValues[WGUndergroundWarrantSystem.Credits], Is.EqualTo(member.BaseWarrant + 25));

            warrants.Worked(body, 100);
            Assert.That(contraband.TurnInValues[WGUndergroundWarrantSystem.Credits], Is.EqualTo(member.MaxWarrant));
            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A TSF pallet holds a member for bail before it takes them; bail paid, it never does.</summary>
    [Test]
    public async Task PalletHoldsMemberForBail()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var warrants = server.System<WGUndergroundWarrantSystem>();
        var heat = server.System<WGUndergroundHeatSystem>();

        var bailed = EntityUid.Invalid;
        var taken = EntityUid.Invalid;
        var console = EntityUid.Invalid;
        var treasury = 0;
        var captures = heat.Captures;
        var gone = new NetUserId(Guid.NewGuid());

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("ContrabandPallet", map.GridCoords);
            console = entMan.SpawnEntity("ComputerContrabandPalletConsole", map.GridCoords);
            bailed = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(bailed);
            // The second one's player has left the body: only the member record says whose it was.
            taken = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(taken).User = gone;
            treasury = heat.Treasury;
            entMan.EventBus.RaiseLocalEvent(console, new ContrabandPalletSellMessage { Actor = console });
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(bailed), Is.False, "The pallet took a member before the bail clock ran out.");
            Assert.That(entMan.Deleted(taken), Is.False);
            var held = entMan.GetComponent<WGUndergroundHeldComponent>(bailed);
            Assert.That(held.Bail, Is.EqualTo(25 * 200));

            Assert.That(warrants.PayBail(bailed), Is.True);
            Assert.That(heat.Treasury, Is.EqualTo(treasury - held.Bail));
            Assert.That(entMan.HasComponent<WGUndergroundHeldComponent>(bailed), Is.False);
            Assert.That(entMan.GetComponent<ContrabandComponent>(bailed).TurnInValues.ContainsKey(WGUndergroundWarrantSystem.Credits), Is.False);

            // The other one's clock runs out unpaid.
            entMan.GetComponent<WGUndergroundHeldComponent>(taken).Until = TimeSpan.Zero;
            entMan.EventBus.RaiseLocalEvent(console, new ContrabandPalletSellMessage { Actor = console });
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(taken), Is.True, "The pallet didn't take a member whose bail went unpaid.");
            Assert.That(entMan.Deleted(bailed), Is.False, "The pallet took a member whose bail was paid.");
            Assert.That(heat.Captures, Is.EqualTo(captures + 1));
            Assert.That(warrants.Served(gone), Is.True, "A capture after the player left the body didn't lock their next one.");

            // Their next body carries the lock: no warrant on it, and the pallet neither holds nor takes it.
            var next = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(next);
            warrants.Claim(next, gone);
            Assert.That(entMan.GetComponent<ContrabandComponent>(next).TurnInValues.ContainsKey(WGUndergroundWarrantSystem.Credits), Is.False);
            Assert.That(warrants.CanTurnIn(next, WGUndergroundWarrantSystem.Credits.Id), Is.False, "The pallet would take a player still serving their hour.");
            Assert.That(entMan.HasComponent<WGUndergroundHeldComponent>(next), Is.False);
            entMan.DeleteEntity(next);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A mark settles for the player even after they left the body, and isn't given to them again or to a tagged new player.</summary>
    [Test]
    public async Task HereticMarkFollowsThePlayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var marks = server.System<WGHereticMarkSystem>();
        var gone = new NetUserId(Guid.NewGuid());

        await server.WaitAssertion(() =>
        {
            var mobState = entMan.System<MobStateSystem>();

            bool Refused(EntityUid target)
            {
                entMan.AddComponent(target, new AddComponentsAfterTimeComponent
                {
                    SelectionTime = TimeSpan.FromDays(1),
                    AddComponentsOnSelection = new ComponentRegistry(),
                });
                var attempt = new WGMarkAttemptEvent();
                entMan.EventBus.RaiseLocalEvent(target, ref attempt);
                return attempt.Cancelled;
            }

            var body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(body).User = gone;
            entMan.AddComponent<HereticComponent>(body);
            Assert.That(marks.Resolved(gone), Is.False);
            mobState.ChangeMobState(body, MobState.Dead);
            Assert.That(marks.Resolved(gone), Is.True, "A heretic who died after their player left wasn't settled.");

            // The same when only the record made as the mark landed says whose body it was.
            var left = new NetUserId(Guid.NewGuid());
            var husk = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(husk).User = left;
            entMan.AddComponent<HereticComponent>(husk);
            entMan.RemoveComponent<WGUndergroundMemberComponent>(husk);
            mobState.ChangeMobState(husk, MobState.Dead);
            Assert.That(marks.Resolved(left), Is.True, "A heretic whose player could no longer be found wasn't settled.");
            entMan.DeleteEntity(husk);

            var next = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(next).User = gone;
            Assert.That(Refused(next), Is.True, "A settled player was marked again.");

            var newbie = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGNewbieComponent>(newbie);
            Assert.That(Refused(newbie), Is.True, "A tagged new player was marked.");

            var other = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(Refused(other), Is.False, "An ordinary mark was refused.");

            foreach (var uid in new[] { body, next, newbie, other })
            {
                entMan.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The cargo console's verbs keep its company lock: an Underground card isn't enough for the TSF.</summary>
    [Test]
    public async Task ConsoleVerbsKeepTheCompanyLock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var terminal = server.System<WGUndergroundTerminalSystem>();

        await server.WaitAssertion(() =>
        {
            var console = entMan.SpawnEntity("ComputerTabletopCargoOrdersUnderground", map.GridCoords);
            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(terminal.Authorized(console, user, out _, out _), Is.False, "The terminal opened to someone with no card.");

            var card = entMan.SpawnEntity("FugitiveIDCard", map.GridCoords);
            entMan.System<SharedAccessSystem>().TrySetTags(card, new ProtoId<AccessLevelPrototype>[] { "Underground", "Godfather" });
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(user, card, checkActionBlocker: false));

            // No company at all is not one of the locked-out companies.
            Assert.That(terminal.Authorized(console, user, out var boss, out var officer), Is.True);
            Assert.That(boss && officer, Is.True);

            var company = entMan.EnsureComponent<CompanyComponent>(user);
            company.CompanyName = "TSF";
            Assert.That(terminal.Authorized(console, user, out _, out _), Is.False, "A TSF player used the terminal with a stolen card.");
            company.CompanyName = "PDV";
            Assert.That(terminal.Authorized(console, user, out _, out _), Is.False);
            company.CompanyName = WGUndergroundHeatSystem.Company;
            Assert.That(terminal.Authorized(console, user, out _, out _), Is.True);

            // A verb built while they were allowed does nothing once they aren't.
            var heat = server.System<WGUndergroundHeatSystem>();
            heat.SetClosed(false);
            var label = server.ResolveDependency<ILocalizationManager>().GetString("wg-underground-verb-close");
            var close = entMan.System<SharedVerbSystem>()
                .GetLocalVerbs(console, user, typeof(AlternativeVerb), force: true)
                .FirstOrDefault(verb => verb.Text == label);
            Assert.That(close, Is.Not.Null, "The Godfather's card didn't get the market verb.");
            company.CompanyName = "TSF";
            close!.Act?.Invoke();
            Assert.That(heat.Closed, Is.False, "A verb built before the company change still ran.");
            company.CompanyName = WGUndergroundHeatSystem.Company;
            close.Act?.Invoke();
            Assert.That(heat.Closed, Is.True);
            heat.SetClosed(false);

            entMan.DeleteEntity(user);
            entMan.DeleteEntity(console);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Arming deploys the turrets, a turret that wasn't up is brought up on the next pass, and standing down retracts them.</summary>
    [Test]
    public async Task ArmedTurretsAreKeptDeployed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var station = server.System<WGUndergroundBaseSystem>();

        var first = EntityUid.Invalid;
        var late = EntityUid.Invalid;
        var drained = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var battery = entMan.System<BatterySystem>();
            first = entMan.SpawnEntity("WGBallisticTurretHeavyAIUnderground", map.GridCoords);
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(first).Enabled, Is.False, "The turrets should start retracted.");

            // One with no power and no shots refuses to deploy when the order goes out.
            drained = entMan.SpawnEntity("WGBallisticTurretHeavyAIUnderground", map.GridCoords);
            battery.SetCharge(drained, 0f);

            station.SetArmed(true);
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(first).Enabled, Is.True);
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(drained).Enabled, Is.False, "A turret with no power or shots deployed.");
            battery.SetCharge(drained, 5000f);

            // One that wasn't up when the order went out, like a turret that had no power.
            late = entMan.SpawnEntity("WGBallisticTurretHeavyAIUnderground", map.GridCoords);
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(late).Enabled, Is.False);
        });
        await pair.RunSeconds(12);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(late).Enabled, Is.True, "An armed station didn't bring a late turret up.");
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(drained).Enabled, Is.True, "A turret that got its charge back stayed down.");
            station.SetArmed(false);
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(first).Enabled, Is.False);
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(late).Enabled, Is.False);
            entMan.DeleteEntity(first);
            entMan.DeleteEntity(late);
            entMan.DeleteEntity(drained);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Only the Sultanate's pallet takes a body, and only a heretic's, for half the live price; it clears the mark and leaves the body.</summary>
    [Test]
    public async Task OnlyTheSultanateTakesABody()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var member = EntityUid.Invalid;
        var heretic = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var mobState = entMan.System<MobStateSystem>();

            // A dead member who was also marked: the TSF's pallet must not pay for the corpse.
            entMan.SpawnEntity("ContrabandPallet", map.GridCoords);
            var tsf = entMan.SpawnEntity("ComputerContrabandPalletConsole", map.GridCoords);
            member = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(member);
            entMan.AddComponent<HereticComponent>(member);
            mobState.ChangeMobState(member, MobState.Dead);
            entMan.EventBus.RaiseLocalEvent(tsf, new ContrabandPalletSellMessage { Actor = tsf });
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(member), Is.False, "The TSF's pallet took a body.");
            entMan.DeleteEntity(member);

            var mobState = entMan.System<MobStateSystem>();
            entMan.SpawnEntity("ContrabandPalletPirate", map.GridCoords);
            var pdv = entMan.SpawnEntity("ComputerContrabandPalletConsolePirate", map.GridCoords);
            heretic = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<ContrabandComponent>(heretic).TurnInValues[WGUndergroundWarrantSystem.Doubloons] = 250;
            entMan.AddComponent<HereticComponent>(heretic);

            // Alive pays in full and a body half, so the hunt isn't worth more as a kill.
            var warrants = entMan.System<WGUndergroundWarrantSystem>();
            var doubloons = WGUndergroundWarrantSystem.Doubloons.Id;
            Assert.That(warrants.TurnInValue(heretic, doubloons, 250), Is.EqualTo(250), "A live heretic didn't pay in full.");
            mobState.ChangeMobState(heretic, MobState.Dead);
            Assert.That(warrants.TurnInValue(heretic, doubloons, 250), Is.EqualTo(125), "A heretic's body didn't pay half.");
            Assert.That(warrants.TurnInValue(heretic, WGUndergroundWarrantSystem.Credits.Id, 250), Is.EqualTo(250), "Only the Sultanate's price depends on the mark.");
            entMan.EventBus.RaiseLocalEvent(pdv, new ContrabandPalletSellMessage { Actor = pdv });
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(heretic), Is.False, "The heretic's body was taken away.");
            Assert.That(entMan.HasComponent<HereticComponent>(heretic), Is.False, "The mark wasn't cleared.");
            Assert.That(entMan.GetComponent<ContrabandComponent>(heretic).TurnInValues.ContainsKey(WGUndergroundWarrantSystem.Doubloons), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The Fixer is an Underground job between the Fugitives and the Godfather, with its loadout and ID.</summary>
    [Test]
    public async Task FixerIsAnUndergroundJob()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var job = protos.Index<JobPrototype>(Fixer);
            Assert.That(job.AssignedCompany, Is.EqualTo(WGUndergroundHeatSystem.Company));
            Assert.That(protos.Index<DepartmentPrototype>("Underground").Roles.Select(r => r.Id), Does.Contain(Fixer));
            Assert.That(WGFactionCrewPolicy.Rank(WGCrewFaction.Underground, Fixer),
                Is.GreaterThan(WGFactionCrewPolicy.Rank(WGCrewFaction.Underground, "Fugitive")));
            Assert.That(WGFactionCrewPolicy.Rank(WGCrewFaction.Underground, Fixer),
                Is.LessThan(WGFactionCrewPolicy.Rank(WGCrewFaction.Underground, "Godfather")));
            Assert.That(protos.HasIndex<RoleLoadoutPrototype>("Job" + Fixer));

            var map = entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var card = entities.SpawnEntity("WGUndergroundFixerIDCard", new MapCoordinates(0, 0, mapId));
            Assert.That(entities.GetComponent<PresetIdCardComponent>(card).JobName?.Id, Is.EqualTo(Fixer));
            foreach (var proto in new[] { "WGUndergroundMarketPass", "WGBallisticTurretHeavyAIUnderground", "WGSpawnPointUndergroundFixer" })
            {
                Assert.That(protos.HasIndex<EntityPrototype>(proto), $"{proto} is missing.");
            }

            entities.DeleteEntity(map);
        });

        await pair.CleanReturnAsync();
    }
}
