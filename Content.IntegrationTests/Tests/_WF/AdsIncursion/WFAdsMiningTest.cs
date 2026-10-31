#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.AdsIncursion.Systems;
using Content.Server._WF.AsteroidMining;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Shared._WF.AsteroidMining;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.AdsIncursion;

/// <summary>Rocks in held space are protected: a miner is warned, left alone if it stops and declared a threat if it goes on.</summary>
[TestOf(typeof(WFAdsCampaignSystem))]
public sealed class WFAdsMiningTest : WFAdsTestBase
{
    private const string Laser = "WFWeaponTurretMiningBeam";

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    /// <summary>A crewed ship with a mining laser, beside a sweep's node in held space.</summary>
    private (EntityUid Ship, EntityUid Gun, EntityUid Sweep) Scene()
    {
        var ads = Server.System<WFAdsCampaignSystem>();
        var node = FoundNode(AdsHome);
        ads.SetTier(1);
        Assert.That(ads.TryLaunchSweep(node), Is.True);
        var ship = SpawnGrid(CentreOf(AdsHome.Neighbour(1)), "Test Hauler");
        Board(ship);
        var gun = SEntMan.SpawnEntity(Laser, new EntityCoordinates(ship, new Vector2(0.5f, 0.5f)));
        return (ship, gun, ads.Sweeps().Single().Owner);
    }

    private void Beam(EntityUid gun, EntityUid rock)
    {
        Server.System<WFMiningBeamSystem>().Pulse(gun, SEntMan.GetComponent<WFMiningBeamEmitterComponent>(gun), rock, 1f, new Vector2(0f, -1f));
    }

    private void Stop(EntityUid gun)
    {
        Server.System<WFMiningBeamSystem>().Stop(gun, SEntMan.GetComponent<WFMiningBeamEmitterComponent>(gun));
    }

    private EntityUid Rock(WFSectorCell cell)
    {
        return SEntMan.SpawnEntity(null, new MapCoordinates(CentreOf(cell), MapData.MapId));
    }

    private WFEncounterShipState SweepShip(EntityUid sweep)
    {
        return SEntMan.GetComponent<WFEncounterComponent>(sweep).Ships["lead"];
    }

    /// <summary>Mining on past the grace period declares the ship to every ADS ship, whatever it told a hail, and brings a sweep.</summary>
    [Test]
    public async Task MiningOnMakesTheShipAThreat()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var zones = Server.System<WFEncounterZoneSystem>();
            var (ship, gun, sweep) = Scene();
            var rock = Rock(AdsHome);

            // It told a hail it was no threat; that doesn't cover mining.
            SEntMan.GetComponent<WFEncounterComponent>(sweep).Interrogated[ship] = STiming.CurTime + TimeSpan.FromMinutes(15);

            Beam(gun, rock);
            ads.CheckMining(Tick);
            Assert.That(AdsState().Miners.ContainsKey(ship), Is.True, "The miner wasn't warned.");
            Assert.That(zones.IsThreat(SweepShip(sweep), ship), Is.False, "A warning alone made it a threat.");

            ads.CheckMining(TimeSpan.FromSeconds(29));
            Assert.That(zones.IsThreat(SweepShip(sweep), ship), Is.False, "It was declared inside the grace period.");

            ads.CheckMining(TimeSpan.FromSeconds(2));
            Assert.That(zones.IsThreat(SweepShip(sweep), ship), Is.True, "Mining on past the grace period didn't declare it.");
            Assert.That(SEntMan.GetComponent<WFAdsSweepComponent>(sweep).Interdicting, Is.EqualTo(ship), "No sweep was sent after it.");

            Stop(gun);
            Disembark();
        });
    }

    /// <summary>A ship that stops after its warning is left alone, and its warning is forgotten in time.</summary>
    [Test]
    public async Task StoppingLeavesTheShipAlone()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var (ship, gun, sweep) = Scene();

            Beam(gun, Rock(AdsHome));
            ads.CheckMining(Tick);
            Stop(gun);
            ads.CheckMining(TimeSpan.FromMinutes(2));

            var state = AdsState();
            Assert.That(state.MiningGrudges.ContainsKey(ship), Is.False, "Time off the rocks counted as mining.");
            Assert.That(Server.System<WFEncounterZoneSystem>().IsThreat(SweepShip(sweep), ship), Is.False, "A ship that stopped was declared.");
            Assert.That(state.Miners.ContainsKey(ship), Is.True, "The warning was forgotten too soon.");

            state.Miners[ship].LastMined = STiming.CurTime - TimeSpan.FromMinutes(6);
            ads.CheckMining(Tick);
            Assert.That(state.Miners.ContainsKey(ship), Is.False, "The warning was never forgotten.");

            Disembark();
        });
    }

    /// <summary>Short bursts add up, so stopping now and then doesn't reset the warning.</summary>
    [Test]
    public async Task BurstsAddUp()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var (ship, gun, _) = Scene();
            var rock = Rock(AdsHome);

            Beam(gun, rock);
            ads.CheckMining(Tick);
            for (var burst = 0; burst < 2; burst++)
            {
                Beam(gun, rock);
                ads.CheckMining(TimeSpan.FromSeconds(16));
                Stop(gun);
                ads.CheckMining(TimeSpan.FromSeconds(16));
            }

            Assert.That(AdsState().MiningGrudges.ContainsKey(ship), Is.True, "Bursts got round the grace period.");
            Disembark();
        });
    }

    /// <summary>A rock outside held space is nobody's business, and a beam on nothing is not mining.</summary>
    [Test]
    public async Task RocksOutsideHeldSpaceAreFree()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var (ship, gun, _) = Scene();

            Beam(gun, Rock(FarCell(AdsHome, 4)));
            ads.CheckMining(TimeSpan.FromSeconds(60));
            Assert.That(AdsState().Miners.ContainsKey(ship), Is.False, "A rock outside held space drew a warning.");

            Stop(gun);
            ads.CheckMining(Tick);
            Assert.That(AdsState().Miners.ContainsKey(ship), Is.False);
            Disembark();
        });
    }
}
