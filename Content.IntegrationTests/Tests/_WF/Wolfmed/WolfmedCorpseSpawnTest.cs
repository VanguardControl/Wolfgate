#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._NF.Medical.Components;
using Content.Server._WF.Wolfmed.Life;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Body.Components;
using Content.Server.Humanoid.Systems;
using Content.Shared._NF.Medical.Prototypes;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// "SOME dead bodies aren't actually spawning as dead bodies." A corpse prototype carries its injuries as preset damage
/// and a medical bounty deals them at startup, before the body has parts; on a wound host both were lost, so salvage
/// and dungeon corpses stood up alive and unhurt and bounty corpses came up alive, some with nothing to treat. They now
/// spawn dead, their injuries on them as wounds over several parts, their Bloodloss as missing blood, not bleeding.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedSpawnInjurySystem))]
public sealed class WolfmedCorpseSpawnTest : GameTest
{
    [TestCase("SalvageHumanCorpse")]
    [TestCase("MobRandomMedicCorpse")]
    [TestCase("DungeonHumanCorpseRandomMedic")]
    public async Task CorpsePrototypeSpawnsDeadWithItsInjuriesTest(string prototype)
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        var parts = 0;
        await Server.WaitAssertion(() =>
        {
            body = SEntMan.SpawnEntity(prototype, map.GridCoords);
            var reference = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            parts = SEntMan.System<SharedBodySystem>().GetBodyChildren(reference).Count();
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            // SalvageHumanCorpse: Bloodloss 49, Asphyxiation 76, Slash 56, Blunt 19.
            var share = Server.CfgMan.GetCVar(WolfmedCVars.SpawnBloodlossBlood);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<MobStateComponent>(body).CurrentState, Is.EqualTo(MobState.Dead),
                    "the corpse spawned alive.");
                Assert.That(WoundedParts(body), Is.GreaterThanOrEqualTo(2), "the corpse's injuries did not spread over the body.");
                Assert.That(Stored(body, "Slash"), Is.GreaterThan(FixedPoint2.New(40)), "the corpse's cuts were lost.");
                Assert.That(SEntMan.System<WolfmedLifeSystem>().GetBlood(body), Is.EqualTo(1f - 49 * share).Within(0.02f),
                    "the corpse's Bloodloss did not become missing blood.");
                Assert.That(SEntMan.GetComponent<BloodstreamComponent>(body).BleedAmount, Is.Zero, "the corpse bleeds.");
                Assert.That(SEntMan.System<SharedBodySystem>().GetBodyChildren(body).Count(), Is.EqualTo(parts),
                    "the corpse spawned missing a part.");
            });
        });

        // It stays dead.
        await RunSeconds(5);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<MobStateComponent>(body).CurrentState, Is.EqualTo(MobState.Dead)));
    }

    /// <summary>The mouse is no wound host: its preset damage still kills it the upstream way.</summary>
    [Test]
    public async Task DeadMouseIsStillDeadTest()
    {
        var map = await Pair.CreateTestMap();
        EntityUid mouse = default;
        await Server.WaitAssertion(() => mouse = SEntMan.SpawnEntity("MobMouseDead", map.GridCoords));
        await RunSeconds(1);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<MobStateComponent>(mouse).CurrentState, Is.EqualTo(MobState.Dead)));
    }

    [TestCase("MurderedSlash", "Slash")]
    [TestCase("MurderedPierce", "Piercing")]
    [TestCase("PlasmaFire", "Heat")]
    public async Task MedicalBountyCorpseSpawnsDeadWithItsInjuriesTest(string bounty, string type)
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await Server.WaitAssertion(() =>
        {
            body = SEntMan.CreateEntityUninitialized("MobHuman", map.GridCoords);
            SEntMan.AddComponent(body, new MedicalBountyComponent { Bounty = SProtoMan.Index<MedicalBountyPrototype>(bounty) });
            SEntMan.InitializeAndStartEntity(body);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var maximum = SProtoMan.Index<MedicalBountyPrototype>(bounty).MaximumDamageToRedeem;
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<MobStateComponent>(body).CurrentState, Is.EqualTo(MobState.Dead),
                    "the bounty corpse spawned alive.");
                Assert.That(Stored(body, type), Is.GreaterThan(FixedPoint2.Zero), "the bounty's injuries were lost.");
                Assert.That(WoundedParts(body), Is.GreaterThanOrEqualTo(3), "the bounty's injuries landed on one part.");
                Assert.That(SEntMan.GetComponent<DamageableComponent>(body).TotalDamage, Is.GreaterThan(FixedPoint2.New(maximum)),
                    "the bounty can be redeemed without treating anything.");
                Assert.That(SEntMan.GetComponent<BloodstreamComponent>(body).BleedAmount, Is.Zero, "the bounty corpse bleeds.");
            });
        });
    }

    /// <summary>Every bounty rolled the way the round rolls them: none comes up alive with nothing to treat.</summary>
    [Test]
    public async Task NoRandomBountyComesUpFreeTest()
    {
        var map = await Pair.CreateTestMap();
        var bodies = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            for (var i = 0; i < 24; i++)
                bodies.Add(SEntMan.System<RandomHumanoidSystem>().SpawnRandomHumanoid("MedicalBounty", map.GridCoords, "bounty"));
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            foreach (var body in bodies)
            {
                var bounty = SEntMan.GetComponent<MedicalBountyComponent>(body).Bounty!;
                var total = SEntMan.GetComponent<DamageableComponent>(body).TotalDamage;
                Assert.That(total, Is.GreaterThan(FixedPoint2.New(bounty.MaximumDamageToRedeem)),
                    $"{bounty.ID} came up redeemable at {total} damage.");
            }
        });
    }

    private int WoundedParts(EntityUid body) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .Count(part => SEntMan.System<WoundSystem>().GetWounds(part.Id).Any());

    private FixedPoint2 Stored(EntityUid body, string type)
    {
        var total = FixedPoint2.Zero;
        foreach (var (part, _) in SEntMan.System<SharedBodySystem>().GetBodyChildren(body))
            total += SEntMan.GetComponent<DamageableComponent>(part).Damage.DamageDict.GetValueOrDefault(type);
        return total;
    }
}
