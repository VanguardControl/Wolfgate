#nullable enable
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Tests._WG.TradeGoods;
using Content.Server._Mono.PDV.Components;
using Content.Server._NF.SectorServices;
using Content.Server._WG.CargoSales;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server._WG.Underground;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Server.Access.Components;
using Content.Server.Radio;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Contraband.Events;
using Content.Shared._NF.Shipyard.Components;
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
using Content.Shared.Radio;
using Content.Shared.Roles;
using Content.Shared.Shuttles.Components;
using Content.Shared.Stacks;
using Content.Shared.Turrets;
using Content.Shared.Verbs;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Underground;

/// <summary>The Underground's Heat, warrants, bail and pallet rules, and its Fixer job.</summary>
[TestFixture]
public sealed class WGUndergroundTest
{
    private const string Fixer = "WGUndergroundFixer";
    private const string Smuggling = "WGTradeGoodContraband";

    /// <summary>Heat climbs through the levels, cools all the time, opens a raid window at the top and can't reopen one straight after.</summary>
    [Test]
    public async Task HeatRisesCoolsAndRaids()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var heat = server.System<WGUndergroundHeatSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            // The Heat belongs to the server, not the map, so a pooled pair can still hold another test's.
            heat.Reset();
            var now = timing.CurTime;
            Assert.That(heat.Level, Is.EqualTo(0));
            Assert.That(heat.PdaLine(), Is.Null, "A round with no Underground station shows no Heat line.");

            heat.Add(6);
            Assert.That(heat.Level, Is.EqualTo(1));
            heat.Add(6);
            Assert.That(heat.Level, Is.EqualTo(2));
            Assert.That(heat.Percent(), Is.EqualTo(60));

            // No quiet spell first: 0.2 a minute from the start, so 14 minutes take 2.8 off.
            heat.Tick(now + TimeSpan.FromMinutes(14));
            Assert.That(heat.Level, Is.EqualTo(1));
            Assert.That(heat.Heat, Is.EqualTo(9.2).Within(0.05));

            heat.Add(11);
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
            heat.Reset();
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

            heat.Reset();
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

            // Pay that couldn't be delivered goes back to the treasury without counting as income.
            var held = heat.Treasury;
            var earned = heat.Earned;
            heat.Refund(500);
            Assert.That(heat.Treasury, Is.EqualTo(held + 500));
            Assert.That(heat.Earned, Is.EqualTo(earned), "A refund was counted as takings.");
            heat.Reset();

            entMan.DeleteEntity(member);
            entMan.DeleteEntity(courier);
            entMan.DeleteEntity(marine);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A member's smuggling crate fetches more the hotter the Underground runs and isn't tipped to security; a courier's
    /// fetches the street price. Nothing extra is paid while a raid window is open.
    /// </summary>
    [Test]
    public async Task HeatEarnsMembersAPremium()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = false });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var heat = server.System<WGUndergroundHeatSystem>();
        var market = server.System<WGTradeMarketSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            if (!entMan.EntityExists(entMan.System<SectorServiceSystem>().GetServiceEntity()))
                entMan.AddComponent<StationSectorServiceHostComponent>(map.MapUid);
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WG test far yard");
            var member = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(member);
            var courier = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var crate = TradeTestWorld.Hauled(entMan, far, map.GridCoords, Smuggling);

            heat.Reset();
            var street = market.Quote(chute, Smuggling)!.Value.Amount;
            Assert.That(market.Quote(chute, Smuggling, crate, member)!.Value.Amount, Is.EqualTo(street), "A member was paid a premium with no Heat.");

            // Halfway to RAID the premium is halfway to its top.
            heat.Add(cfg.GetCVar(WGUndergroundCVars.HeatRaid) / 2);
            var half = 1f + (cfg.GetCVar(WGUndergroundCVars.PremiumMax) - 1f) / 2;
            Assert.That(heat.Premium(), Is.EqualTo(half).Within(0.01));
            var quote = market.Quote(chute, Smuggling, crate, member)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(quote.Amount, Is.EqualTo((int) MathF.Round(street * half)).Within(1));
                Assert.That(quote.Reason, Is.EqualTo((LocId) "wg-underground-reason-premium"));
                Assert.That(market.Quote(chute, Smuggling, crate, courier)!.Value.Amount, Is.EqualTo(street), "A courier was paid the Underground's premium.");
                Assert.That(heat.PremiumShare(), Is.LessThan(cfg.GetCVar(WGUndergroundCVars.PremiumTelecrystals)), "Telecrystals were multiplied below their line.");
            });

            // Customs keeps a member's crates to itself, but not their narcotics or a courier's crates.
            var tip = new WGNarcoticTipAttemptEvent(member, "wg-trade-contraband-tip-goods");
            entMan.EventBus.RaiseEvent(EventSource.Local, ref tip);
            var drugs = new WGNarcoticTipAttemptEvent(member, WGNarcoticHeatSystem.Narcotics);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref drugs);
            var courierTip = new WGNarcoticTipAttemptEvent(courier, "wg-trade-contraband-tip-goods");
            entMan.EventBus.RaiseEvent(EventSource.Local, ref courierTip);
            Assert.Multiple(() =>
            {
                Assert.That(tip.Cancelled, Is.True, "A member's crate was tipped to security.");
                Assert.That(drugs.Cancelled, Is.False, "A member's narcotics went untipped.");
                Assert.That(courierTip.Cancelled, Is.False, "A courier's crate went untipped.");
            });

            heat.Add(cfg.GetCVar(WGUndergroundCVars.HeatRaid));
            Assert.That(heat.RaidEnds, Is.Not.Null);
            Assert.That(heat.Premium(), Is.EqualTo(1f), "A premium was paid during a raid window.");
            heat.Reset();
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A switched-on ship jammer stops radio sent from its own grid and nowhere else.</summary>
    [Test]
    public async Task ShipJammerSilencesItsGrid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var timing = server.ResolveDependency<IGameTiming>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var jammers = entMan.System<WGShipJammerSystem>();
            var ship = map.Grid.Owner;
            var other = TradeTestWorld.Grid(entMan, map, new Vector2(500f, 0f), "WG test other ship");
            var aboard = entMan.SpawnEntity(null, map.GridCoords);
            var jammerUid = entMan.SpawnEntity("WGShipJammer", map.GridCoords);
            var jammer = entMan.GetComponent<WGShipJammerComponent>(jammerUid);
            var common = protos.Index<RadioChannelPrototype>("Common");

            Assert.That(jammers.Jammed(ship), Is.False, "A jammer that was never switched on jammed.");
            jammer.Until = timing.CurTime + TimeSpan.FromMinutes(10);
            var ev = new RadioSendAttemptEvent(common, aboard, common.Frequency);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            Assert.Multiple(() =>
            {
                Assert.That(jammers.Jammed(ship), Is.True);
                Assert.That(ev.Cancelled, Is.True, "The jammer let radio off its own ship.");
                Assert.That(jammers.Jammed(other), Is.False, "The jammer silenced another ship.");
            });

            jammer.Until = timing.CurTime;
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var left = entMan.EntityQuery<WGShipJammerComponent>().Count();
            Assert.That(left, Is.Zero, "A spent jammer didn't burn out.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The station jumps to a new place in its band, then not again for an hour. It never leaves while a raid window
    /// is open, even one that opens mid-spool, and a jump with nowhere to go is refused.
    /// </summary>
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
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var home = entMan.AddComponent<WGUndergroundBaseComponent>(grid);
            var from = xforms.GetWorldPosition(grid);

            heat.Reset();

            // Nowhere in the band is far enough away: the jump is refused and nothing is charged for it.
            cfg.SetCVar(WGUndergroundCVars.HopMove, 100000f);
            Assert.That(station.TryHop((grid, home), out var refusal), Is.False, "The station jumped with nowhere to go.");
            Assert.That(refusal, Is.EqualTo(Loc.GetString("wg-underground-hop-nowhere")));
            Assert.That(entMan.HasComponent<FTLComponent>(grid), Is.False);
            Assert.That(home.NextHop, Is.EqualTo(TimeSpan.Zero));
            cfg.SetCVar(WGUndergroundCVars.HopMove, 10000f);

            Assert.That(station.TryHop((grid, home), out refusal), Is.True, refusal);
            Assert.That(entMan.TryGetComponent<FTLComponent>(grid, out var ftl), "The station didn't start its jump.");
            var target = xforms.ToMapCoordinates(ftl!.TargetCoordinates).Position;
            Assert.That(target.Length(), Is.InRange(cfg.GetCVar(WGUndergroundCVars.HopMinRange), cfg.GetCVar(WGUndergroundCVars.HopMaxRange)));
            Assert.That((target - from).Length(), Is.GreaterThanOrEqualTo(cfg.GetCVar(WGUndergroundCVars.HopMove)));

            // A raid that opens while the drive is spooling holds the station and gives the hour back.
            heat.Add(100);
            Assert.That(heat.RaidEnds, Is.Not.Null);
            Assert.That(entMan.HasComponent<FTLComponent>(grid), Is.False, "The station left during a raid window.");
            Assert.That(home.NextHop, Is.LessThanOrEqualTo(timing.CurTime), "A jump the raid called off still cost the hour.");

            // The raid keeps the turrets wanted whatever the Godfather orders, which is what the console reports.
            station.SetArmed(false);
            Assert.That(station.WantTurrets, Is.True, "Standing the turrets down lowered them during a raid.");
            Assert.That(station.TryHop((grid, home), out refusal), Is.False, "The station jumped out of a raid.");
            Assert.That(refusal, Is.EqualTo(Loc.GetString("wg-underground-hop-raid")));
            heat.Tick(heat.RaidEnds!.Value);

            Assert.That(station.TryHop((grid, home), out refusal), Is.True, refusal);

            // The hour runs from the jump, whatever became of it.
            Assert.That(server.System<ShuttleSystem>().WgAbortFtlSpool(grid));
            Assert.That(station.TryHop((grid, home), out refusal), Is.False, "The station jumped twice in an hour.");
            Assert.That(refusal, Is.EqualTo(Loc.GetString("wg-underground-hop-wait", ("minutes", 60))));

            home.NextHop = TimeSpan.Zero;
            Assert.That(station.CanHop((grid, home), out _), "The station can't jump once its hour is up.");
            heat.Reset();
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
            var loc = server.ResolveDependency<ILocalizationManager>();
            var verbs = entMan.System<SharedVerbSystem>();
            var label = loc.GetString("wg-underground-verb-close");
            var menu = verbs.GetLocalVerbs(console, user, typeof(AlternativeVerb), force: true);
            var close = menu.FirstOrDefault(verb => verb.Text == label);
            Assert.That(close, Is.Not.Null, "The Godfather's card didn't get the market verb.");

            // A picked verb is matched on its priority, so a bail verb turning up above must not move this one.
            var prisoner = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(prisoner);
            entMan.AddComponent<WGUndergroundHeldComponent>(prisoner);
            var withHold = verbs.GetLocalVerbs(console, user, typeof(AlternativeVerb), force: true);
            Assert.That(withHold, Has.Count.EqualTo(menu.Count + 1), "A held prisoner didn't add a bail verb.");
            Assert.That(withHold.First(verb => verb.Text == label).Priority, Is.EqualTo(close!.Priority), "A bail verb moved the market verb, so a menu opened before it would miss.");
            entMan.DeleteEntity(prisoner);

            company.CompanyName = "TSF";
            close!.Act?.Invoke();
            Assert.That(heat.Closed, Is.False, "A verb built before the company change still ran.");
            company.CompanyName = WGUndergroundHeatSystem.Company;
            close.Act?.Invoke();
            Assert.That(heat.Closed, Is.True);

            // The verb closes the market, as its label said; picked again from the same menu it doesn't reopen it.
            close.Act?.Invoke();
            Assert.That(heat.Closed, Is.True, "A second close from the same menu reopened the market.");
            heat.SetClosed(false);

            // A vendetta needs its ship in reach when the verb is picked, not only when the menu was built.
            var maps = entMan.System<SharedMapSystem>();
            var xforms = entMan.System<SharedTransformSystem>();
            var ship = maps.CreateGridEntity(map.MapId);
            maps.SetTile(ship.Owner, ship.Comp, Vector2i.Zero, map.Tile.Tile);
            entMan.AddComponent<ShuttleDeedComponent>(ship);
            xforms.SetWorldPosition(ship.Owner, new System.Numerics.Vector2(200f, 0f));
            Assert.That(terminal.InVendettaReach(console, ship), Is.True, "A ship beside the station can't be named.");
            xforms.SetWorldPosition(ship.Owner, new System.Numerics.Vector2(5000f, 0f));
            Assert.That(terminal.InVendettaReach(console, ship), Is.False, "A ship that has left can still be named.");
            Assert.That(terminal.InVendettaReach(console, map.Grid.Owner), Is.False, "The station can be named against itself.");

            // The same through the verb: built with the ship beside the station, picked after it has gone.
            xforms.SetWorldPosition(ship.Owner, new System.Numerics.Vector2(200f, 0f));
            var named = loc.GetString("wg-underground-verb-vendetta", ("ship", entMan.GetComponent<MetaDataComponent>(ship).EntityName));
            var vendetta = verbs.GetLocalVerbs(console, user, typeof(AlternativeVerb), force: true).FirstOrDefault(verb => verb.Text == named);
            Assert.That(vendetta, Is.Not.Null, "A ship beside the station got no vendetta verb.");
            xforms.SetWorldPosition(ship.Owner, new System.Numerics.Vector2(5000f, 0f));
            vendetta!.Act?.Invoke();
            Assert.That(terminal.UnderVendetta(ship), Is.False, "A vendetta was declared on a ship that had left.");
            xforms.SetWorldPosition(ship.Owner, new System.Numerics.Vector2(200f, 0f));
            vendetta.Act?.Invoke();
            Assert.That(terminal.UnderVendetta(ship), Is.True, "A vendetta wasn't declared on a ship in reach.");
            server.System<WGUndergroundBaseSystem>().SetArmed(false);
            entMan.DeleteEntity(ship);

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
        var cfg = server.ResolveDependency<IConfigurationManager>();

        var first = EntityUid.Invalid;
        var late = EntityUid.Invalid;
        var drained = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            // No warning for now: the order goes straight to the turrets.
            cfg.SetCVar(WGUndergroundCVars.TurretWarning, 0f);
            server.System<WGUndergroundHeatSystem>().Reset();
            var battery = entMan.System<BatterySystem>();
            first = entMan.SpawnEntity("WGBallisticTurretHeavyAIUnderground", map.GridCoords);
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(first).Enabled, Is.False, "The turrets should start retracted.");

            // One with no power and no shots refuses to deploy when the order goes out.
            drained = entMan.SpawnEntity("WGBallisticTurretHeavyAIUnderground", map.GridCoords);
            battery.SetCharge(drained, 0f);

            // A guest who docks while the station is open hears nothing. Docking only reads what the two ends
            // carry, so bare stand-ins do for the station and the ships, and are gone before a tick runs.
            var home = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<WGUndergroundBaseComponent>(home);
            var guest = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var ours = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<CompanyComponent>(ours).CompanyName = WGUndergroundHeatSystem.Company;
            station.Docked(home, guest);
            Assert.That(station.ToldClosed(guest), Is.False, "A ship was warned off an open station.");

            station.SetArmed(true);
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(first).Enabled, Is.True);
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(drained).Enabled, Is.False, "A turret with no power or shots deployed.");

            // One who docks after the turrets are up missed the warning, so the ship is told on arrival.
            station.Docked(guest, home);
            station.Docked(home, ours);
            Assert.That(station.ToldClosed(guest), Is.True, "A ship that docked with the turrets up wasn't told the station is closed.");
            Assert.That(station.ToldClosed(ours), Is.False, "One of the Underground's own ships was warned off.");
            entMan.DeleteEntity(home);
            entMan.DeleteEntity(guest);
            entMan.DeleteEntity(ours);
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

            // With a warning set, guests get that long to leave before the turrets come up.
            cfg.SetCVar(WGUndergroundCVars.TurretWarning, 5f);
            station.SetArmed(true);
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(first).Enabled, Is.False, "The turrets came up with no warning.");
        });
        await pair.RunSeconds(7);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(first).Enabled, Is.True, "The turrets stayed down after the warning.");
            station.SetArmed(false);
            Assert.That(entMan.GetComponent<DeployableTurretComponent>(first).Enabled, Is.False);
            cfg.SetCVar(WGUndergroundCVars.TurretWarning, 30f);
            entMan.DeleteEntity(first);
            entMan.DeleteEntity(late);
            entMan.DeleteEntity(drained);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A warning for those aboard reaches a player on a docked ship and nobody elsewhere; a faction message reaches members wherever they are.</summary>
    [Test]
    public async Task WarningsReachThoseAboard()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var heat = server.System<WGUndergroundHeatSystem>();

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            // The test map's own grid stands for the docked ship; anything else stands for another.
            var guest = map.Grid.Owner;
            var elsewhere = entMan.SpawnEntity(null, MapCoordinates.Nullspace);

            var body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(server.PlayerMan.SetAttachedEntity(session, body, true), "The player didn't take the body.");

            var aboard = new HashSet<EntityUid> { guest };
            Assert.That(heat.Recipients(aboard, false).Recipients, Does.Contain(session), "A player on the docked ship missed the warning.");
            Assert.That(heat.Recipients(new HashSet<EntityUid> { elsewhere }, false).Recipients, Does.Not.Contain(session), "A player on another ship got the warning.");
            Assert.That(heat.Recipients(null, true).Recipients, Does.Not.Contain(session), "Someone outside the Underground got a faction message.");

            entMan.EnsureComponent<CompanyComponent>(body).CompanyName = WGUndergroundHeatSystem.Company;
            Assert.That(heat.Recipients(null, true).Recipients, Does.Contain(session), "A member missed a faction message.");

            server.PlayerMan.SetAttachedEntity(session, null, true);
            entMan.DeleteEntity(body);
            entMan.DeleteEntity(elsewhere);
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
        var pdvConsole = EntityUid.Invalid;
        var crate = EntityUid.Invalid;
        var boxed = EntityUid.Invalid;
        var outer = EntityUid.Invalid;
        var inner = EntityUid.Invalid;
        var nested = EntityUid.Invalid;

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
            pdvConsole = pdv;
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

            // What the console paid out for the body: half of the 250 on the mark.
            var paid = 0;
            var stacks = entMan.EntityQueryEnumerator<StackComponent, TransformComponent>();
            while (stacks.MoveNext(out _, out var stack, out var where))
            {
                if (where.MapID == map.MapId && stack.StackTypeId == WGUndergroundWarrantSystem.Doubloons.Id)
                    paid += stack.Count;
            }

            Assert.That(paid, Is.EqualTo(125), "The pallet didn't pay half for a heretic's body.");

            // A marked heretic shut in a crate the pallet buys: the crate goes, the heretic doesn't.
            crate = entMan.SpawnEntity("CrateGenericSteel", map.GridCoords);
            entMan.EnsureComponent<ContrabandComponent>(crate).TurnInValues[WGUndergroundWarrantSystem.Doubloons] = 10;
            boxed = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<ContrabandComponent>(boxed).TurnInValues[WGUndergroundWarrantSystem.Doubloons] = 250;
            entMan.AddComponent<HereticComponent>(boxed);
            var storage = entMan.System<EntityStorageSystem>();
            Assert.That(storage.Insert(boxed, crate), "The heretic didn't go into the crate.");

            // And one two crates deep: out of the inner crate must not mean into the outer one.
            outer = entMan.SpawnEntity("CrateGenericSteel", map.GridCoords);
            entMan.EnsureComponent<ContrabandComponent>(outer).TurnInValues[WGUndergroundWarrantSystem.Doubloons] = 10;
            inner = entMan.SpawnEntity("CrateGenericSteel", map.GridCoords);
            nested = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<HereticComponent>(nested);
            Assert.That(storage.Insert(nested, inner), "The heretic didn't go into the inner crate.");
            Assert.That(storage.Insert(inner, outer), "The inner crate didn't go into the outer one.");
            entMan.EventBus.RaiseLocalEvent(pdvConsole, new ContrabandPalletSellMessage { Actor = pdvConsole });
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // Out in the open means on the grid or, once the crowd on this one tile has shoved them off it, the map.
            var grid = map.Grid.Owner;
            var containers = entMan.System<SharedContainerSystem>();
            Assert.That(entMan.Deleted(crate), Is.True, "The crate didn't sell.");
            Assert.That(entMan.Deleted(boxed), Is.False, "A heretic was deleted inside a sold crate.");
            Assert.That(entMan.HasComponent<HereticComponent>(boxed), Is.True, "A heretic nobody handed in lost the mark.");
            Assert.That(containers.IsEntityInContainer(boxed), Is.False, "The heretic is still shut in something.");
            Assert.That(entMan.GetComponent<TransformComponent>(boxed).ParentUid, Is.EqualTo(grid).Or.EqualTo(map.MapUid), "The heretic wasn't left out in the open.");

            Assert.That(entMan.Deleted(outer), Is.True, "The outer crate didn't sell.");
            Assert.That(entMan.Deleted(inner), Is.True, "The inner crate outlived the one it was in.");
            Assert.That(entMan.Deleted(nested), Is.False, "A heretic two crates deep was deleted with them.");
            Assert.That(containers.IsEntityInContainer(nested), Is.False, "The nested heretic is still shut in something.");
            Assert.That(entMan.GetComponent<TransformComponent>(nested).ParentUid, Is.EqualTo(grid).Or.EqualTo(map.MapUid), "The nested heretic wasn't left out in the open.");
            entMan.DeleteEntity(nested);
            entMan.DeleteEntity(boxed);
            entMan.DeleteEntity(heretic);
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
