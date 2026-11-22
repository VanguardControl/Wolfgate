#nullable enable
using System.Linq;
using Content.Server._Mono.PDV.Components;
using Content.Server._WG.Underground;
using Content.Server.Access.Components;
using Content.Shared._NF.Contraband.Events;
using Content.Shared._WG.RoundSetup;
using Content.Shared.Contraband;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
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

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("ContrabandPallet", map.GridCoords);
            console = entMan.SpawnEntity("ComputerContrabandPalletConsole", map.GridCoords);
            bailed = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(bailed);
            taken = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGUndergroundMemberComponent>(taken);
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
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Only the Sultanate's pallet takes a body, and only a heretic's; it clears the mark and leaves the body.</summary>
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
            mobState.ChangeMobState(heretic, MobState.Dead);
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
