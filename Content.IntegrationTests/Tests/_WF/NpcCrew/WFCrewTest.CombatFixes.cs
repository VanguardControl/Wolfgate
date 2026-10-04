#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ShipShields;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    private int Ticks(double seconds) => (int) Math.Ceiling(seconds * STiming.TickRate);

    /// <summary>A friendly-faction mob that attacks crew loses protection from their hand weapons and cannons.</summary>
    [Test]
    public async Task CrewProtectionEndsForAttackers()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = ConvoyCrew(deck, "WFCrewDeckhand", "victims", new Vector2(1.5f));
            var attacker = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(3.5f)));
            var factions = Server.System<NpcFactionSystem>();
            factions.ClearFactions(attacker);
            foreach (var faction in SEntMan.GetComponent<NpcFactionMemberComponent>(crew).Factions)
                factions.AddFaction(attacker, faction.Id);
            var gun = SEntMan.SpawnAtPosition(null, new EntityCoordinates(deck, new Vector2(4.5f)));
            var protection = Server.System<WFCrewFriendlyFireSystem>();
            protection.TrackWeapon(gun, crew);
            Assert.That(protection.Protected(crew, attacker), Is.True);
            Assert.That(protection.Protected(gun, attacker), Is.True, "Mobs aboard the crew's own ship are sheltered from its cannons.");

            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 5 } };
            var damageable = Server.System<DamageableSystem>();
            damageable.TryChangeDamage(crew, damage, origin: attacker);
            Assert.That(protection.Protected(crew, attacker), Is.False, "An attacker loses hand-weapon protection.");
            Assert.That(protection.Protected(gun, attacker), Is.False, "An attacker loses cannon protection.");
            var before = SEntMan.GetComponent<DamageableComponent>(attacker).TotalDamage;
            damageable.TryChangeDamage(attacker, damage, ignoreResistances: true, origin: crew);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(attacker).TotalDamage > before, Is.True);
        });
    }

    /// <summary>Crews sharing a group label but serving different ships are separate crews.</summary>
    [Test]
    public async Task CrewSameLabelOnAnotherShipIsNotAnAlly()
    {
        var home = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var other = await CreateDeck(new Vector2(6, 12), 5, gravity: true);
        EntityUid guard = default, boarder = default;
        await Server.WaitAssertion(() =>
        {
            guard = ConvoyCrew(home, "WFCrewDeckhand", "crew", new Vector2(1.5f));
            var mate = ConvoyCrew(home, "WFCrewDeckhand", "crew", new Vector2(3.5f, 1.5f));
            boarder = ConvoyCrew(other, "WFCrewDeckhand", "crew", new Vector2(1.5f));
            Server.System<NpcFactionSystem>().ClearFactions(boarder);
            SEntMan.EnsureComponent<WFCrewSecurityComponent>(guard).Boarding = WFCrewSecurityResponse.Hostile;
            var crews = Server.System<WFCrewSystem>();
            Assert.That(crews.SameCrew(guard, mate), Is.True);
            Assert.That(crews.SameCrew(guard, boarder), Is.False);
            Server.System<SharedTransformSystem>().SetCoordinates(boarder, new EntityCoordinates(home, new Vector2(2.5f, 1.5f)));
            Assert.That(crews.SameCrew(guard, boarder), Is.False, "Boarding another ship does not change a crewman's home.");
            var protection = Server.System<WFCrewFriendlyFireSystem>();
            Assert.That(protection.Protected(guard, mate), Is.True);
            Assert.That(protection.Protected(guard, boarder), Is.False);
        });
        await RunTicks(Ticks(1.5));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<FactionExceptionComponent>(guard, out var exceptions)
                && exceptions.Hostiles.Any(target => target == boarder), Is.True,
                "A same-label crewman from another ship is a boarder.");
        });
    }

    /// <summary>A ship counts as disabled only after 10 s without thrust or weapons; regained thrust restarts the count.</summary>
    [Test]
    public async Task ShipDisabledNeedsSustainedIncapacity()
    {
        var ship = await CreateDeck(new Vector2(20, 0), 3, gravity: true);
        await Server.WaitPost(() => SEntMan.EnsureComponent<ShuttleComponent>(ship));
        Assert.That(await Watch(10f, 1), Is.False);
        Assert.That(await Watch(0f, 6), Is.False, "Six seconds without thrust is not yet disabled.");
        Assert.That(await Watch(10f, 3), Is.False);
        Assert.That(await Watch(0f, 6), Is.False, "Regained thrust restarts the count.");
        Assert.That(await Watch(0f, 8), Is.True, "Ten seconds without thrust or weapons disables the ship.");

        // Holds the thrust for a while, reading the status as often as gunners do; true if it ever read disabled.
        async Task<bool> Watch(float thrust, double seconds)
        {
            var disabled = false;
            for (var waited = 0; waited < Ticks(seconds); waited += 10)
            {
                await Server.WaitPost(() =>
                {
                    SEntMan.GetComponent<ShuttleComponent>(ship).LinearThrust[0] = thrust;
                    disabled |= Server.System<WFCrewShipStatusSystem>().IsDisabled(ship);
                });
                await RunTicks(10);
            }
            return disabled;
        }
    }

    /// <summary>Each attacking vessel expires on its own clock, and repeat fire from a known attacker raises no new alert.</summary>
    [Test]
    public async Task CrewShipThreatsExpirePerVessel()
    {
        var deck = await CreateDeck(new Vector2(500, 500), 5, true);
        var first = await CreateDeck(new Vector2(600, 500), 5, true);
        var second = await CreateDeck(new Vector2(700, 500), 5, true);
        var alerts = Server.System<WFCrewAlertSystem>();
        var observer = Server.System<CrewAlertObserver>();
        await Server.WaitAssertion(() =>
        {
            var started = observer.Started;
            alerts.ReportShipThreat(deck, "expiry", first);
            alerts.ReportShipThreat(deck, "expiry", first);
            Assert.That(observer.Started, Is.EqualTo(started + 1), "A known attacker only extends its window.");
        });
        await RunTicks(Ticks(30));
        await Server.WaitAssertion(() =>
        {
            alerts.ReportShipThreat(deck, "expiry", second);
            Assert.That(alerts.GetHostileShips(deck, "expiry"), Is.EquivalentTo(new[] { first, second }));
        });
        await RunTicks(Ticks(32));
        await Server.WaitAssertion(() =>
        {
            Assert.That(alerts.GetHostileShips(deck, "expiry"), Is.EquivalentTo(new[] { second }),
                "The first attacker's window closed although the second still fires.");
            Assert.That(alerts.IsHostileShip(deck, "expiry", first), Is.False);
            Assert.That(alerts.IsHostileShip(deck, "expiry", second), Is.True);
        });
    }

    /// <summary>A ship rammed at speed takes the rammer for an attacker; a bump is let pass.</summary>
    [Test]
    public async Task RammingIsAnAttackAndBumpingIsNot()
    {
        var deck = await CreateDeck(new Vector2(500, 900), 5, true);
        var rammer = await CreateDeck(new Vector2(600, 900), 5, true);
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WFCrewPilot", "rammed");
            var ramming = Server.System<WFCrewRammingSystem>();
            var alerts = Server.System<WFCrewAlertSystem>();
            Assert.That(ramming.Ram(deck, rammer, WFCrewRammingSystem.RamSpeed - 1f), Is.False);
            Assert.That(alerts.IsHostileShip(deck, "rammed", rammer), Is.False, "A bump is no attack.");
            Assert.That(ramming.Ram(deck, rammer, WFCrewRammingSystem.RamSpeed + 1f), Is.True);
            Assert.That(alerts.IsHostileShip(deck, "rammed", rammer), Is.True, "A ramming is.");
            Assert.That(ramming.Ram(rammer, deck, WFCrewRammingSystem.RamSpeed + 1f), Is.False, "A crew's own clumsy helm starts no fight.");
        });
    }

    /// <summary>A formation partner that fires on another member's hull becomes that ship's attacker.</summary>
    [Test]
    public async Task FormationPartnerFiringOnHullBecomesHostile()
    {
        var leader = await CreateDeck(new Vector2(500, 500), 7, true);
        var escort = await CreateDeck(new Vector2(600, 500), 7, true);
        await Server.WaitAssertion(() =>
        {
            var pilot = ConvoyCrew(escort, "WFCrewPilot", "partner-escort");
            ConvoyCrew(leader, "WFCrewRadioOperator", "partner-ward");
            Server.System<WFPilotDutySystem>().Escort(pilot, leader, 100);
            var escorts = Server.System<WFCrewEscortSystem>();
            var alerts = Server.System<WFCrewAlertSystem>();
            Assert.That(escorts.AreInFormation(leader, escort), Is.True);
            var hit = new WFCrewHullHitEvent(leader, escort);
            SEntMan.EventBus.RaiseLocalEvent(leader, ref hit, true);
            Assert.That(alerts.GetHostileShips(leader, "partner-ward"), Is.EquivalentTo(new[] { escort }));
            Assert.That(alerts.IsHostileShip(leader, "partner-ward", escort), Is.True);
            Assert.That(alerts.GetHostileShips(escort, "partner-escort"), Is.Empty, "Only the victim turns on its partner.");
            Assert.That(escorts.AreInFormation(leader, escort), Is.True, "The assignment itself survives the attack window.");
        });
    }

    /// <summary>A carried gun fired at a shield is not a ship attack; a mounted weapon is.</summary>
    [Test]
    public async Task HandheldShieldHitsAreNotShipAttacks()
    {
        var deck = await CreateDeck(new Vector2(500, 500), 7, true);
        var attacker = await CreateDeck(new Vector2(600, 500), 7, true);
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WFCrewRadioOperator", "shield-target");
            var alerts = Server.System<WFCrewAlertSystem>();
            var shooter = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(attacker, new Vector2(2.5f)));
            var carried = SEntMan.SpawnAttachedTo(null, new EntityCoordinates(shooter, Vector2.Zero));
            var handheld = new WFShipShieldAttackedEvent(deck, attacker, shooter, carried);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref handheld, true);
            Assert.That(alerts.GetHostileShips(deck, "shield-target"), Is.Empty, "A handheld shot must not frame the shooter's ship.");
            var mounted = SEntMan.SpawnAtPosition(null, new EntityCoordinates(attacker, new Vector2(4.5f)));
            var volley = new WFShipShieldAttackedEvent(deck, attacker, null, mounted);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref volley, true);
            Assert.That(alerts.GetHostileShips(deck, "shield-target"), Is.EquivalentTo(new[] { attacker }));
        });
    }

    /// <summary>Ship hitscan damage to a hull alerts the crew the way a projectile impact does.</summary>
    [Test]
    public async Task ShipBeamHullHitAlertsCrew()
    {
        var deck = await CreateDeck(new Vector2(500, 500), 7, true);
        var attacker = await CreateDeck(new Vector2(600, 500), 7, true);
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WFCrewRadioOperator", "beam-target");
            var wall = SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(4.5f)));
            if (!SEntMan.GetComponent<TransformComponent>(wall).Anchored)
                Server.System<SharedTransformSystem>().AnchorEntity(wall);
            Assert.That(SEntMan.GetComponent<TransformComponent>(wall).Anchored, Is.True, "Precondition: the hull wall is anchored.");
            var battery = SEntMan.SpawnAtPosition(null, new EntityCoordinates(attacker, new Vector2(2.5f)));
            var beam = SEntMan.SpawnAtPosition(null, new EntityCoordinates(attacker, new Vector2(3.5f)));
            SEntMan.EnsureComponent<HitscanBasicDamageComponent>(beam);
            var damage = new DamageSpecifier { DamageDict = { ["Heat"] = 5 } };
            Server.System<DamageableSystem>().TryChangeDamage(wall, damage, ignoreResistances: true, origin: battery, tool: beam);
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(deck, "beam-target"), Is.EquivalentTo(new[] { attacker }));
        });
    }

    /// <summary>A weapon fired on its own keeps a crew attribution only while that crewman still commands it.</summary>
    [Test]
    public async Task CannonAttributionEndsWithItsGunner()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var deckhand = ConvoyCrew(deck, "WFCrewDeckhand", "cannoneers", new Vector2(1.5f));
            var gunner = ConvoyCrew(deck, "WFCrewGunner", "cannoneers", new Vector2(2.5f, 1.5f));
            var cannon = SEntMan.SpawnAtPosition("WFTestCrewCannon", new EntityCoordinates(deck, new Vector2(3.5f)));
            var protection = Server.System<WFCrewFriendlyFireSystem>();
            protection.TrackWeapon(cannon, deckhand);
            Assert.That(Attempt(), Is.True, "The commanding crewman's own burst stays attributed.");
            protection.TrackWeapon(cannon, gunner);
            Assert.That(Attempt(), Is.False, "A gunner away from its console no longer commands the weapon.");
            protection.TrackWeapon(cannon, deckhand);
            Server.System<MobStateSystem>().ChangeMobState(deckhand, MobState.Dead);
            Assert.That(Attempt(), Is.False, "Later fire must not inherit a dead crewman's alliances.");

            bool Attempt()
            {
                var shot = new ShotAttemptedEvent { User = cannon, Used = (cannon, SEntMan.GetComponent<GunComponent>(cannon)) };
                SEntMan.EventBus.RaiseLocalEvent(cannon, ref shot);
                return SEntMan.HasComponent<WFCrewShipFireComponent>(cannon);
            }
        });
    }

    /// <summary>An expired attack memory is pruned and no longer strips hostility a boarding rule still owns.</summary>
    [Test]
    public async Task ExpiredRetaliationKeepsBoardingHostility()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        EntityUid guard = default, boarder = default;
        var memory = 0.0;
        await Server.WaitAssertion(() =>
        {
            guard = ConvoyCrew(deck, "WFCrewDeckhand", "keepers", new Vector2(1.5f));
            boarder = ConvoyCrew(deck, "WFCrewDeckhand", "raiders", new Vector2(2.5f));
            Server.System<NpcFactionSystem>().ClearFactions(boarder);
            SEntMan.EnsureComponent<WFCrewSecurityComponent>(guard).Boarding = WFCrewSecurityResponse.Hostile;
            var retaliation = SEntMan.GetComponent<NPCRetaliationComponent>(guard);
            Assert.That(Server.System<NPCRetaliationSystem>().TryRetaliate((guard, retaliation), boarder), Is.True);
            memory = retaliation.AttackMemoryLength!.Value.TotalSeconds;
            // A boarder in sight is not forgotten by himself, so the memory is ended by hand.
            var held = retaliation.AttackMemories;
            held[boarder] = Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime;
        });
        await RunTicks(Ticks(2));
        await Server.WaitAssertion(() =>
        {
            var memories = SEntMan.GetComponent<NPCRetaliationComponent>(guard).AttackMemories;
            Assert.That(memories.ContainsKey(boarder), Is.False, "Expired attack memories are pruned.");
            Assert.That(Server.System<WFCrewSecuritySystem>().IsHostileVisitor(guard, boarder), Is.True);
            Assert.That(Server.System<NpcFactionSystem>().GetHostiles(guard), Does.Contain(boarder),
                "The boarding rule's hostility survives the memory's expiry.");
        });
    }
}
