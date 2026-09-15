using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._EinsteinEngines.Silicon.WeldingHealable; // WOLFGATE: the IPC repair gate lives here.
using Content.Server._EinsteinEngines.Silicon.WeldingHealing;
using Content.Server.Body.Components; // WOLFGATE: BloodstreamComponent is server-only here.
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Body;
using Content.Shared._Onyx.Chemistry.Circulation;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE: D8.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: D12 damage facade.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Phase 5's whole-mob behaviour: the IPC and protogen hosts that PROTO Q and EXT 3 create, the two tool
/// edits that keep an IPC repairable, the gib ceilings, and the prototype-wide circulatory-stream invariant.
/// </summary>
/// <remarks>
/// <para>PLAN5 §6.2 T-P5-2 / -3 / -9 / -10 / -11 / -12 / -14 / -20 (WP13-6).</para>
/// <para>
/// Trap 8 governs every destruction assertion: <c>GibPartBehavior</c> ends in <c>QueueDel</c>
/// (<c>GibbingSystem.TryGibEntityWithRef</c>, <c>BodySystem.GibPart</c>), which is only flushed on a tick, so
/// the damage and the <c>Deleted</c> check never share a block. Trap 7 governs every severing assertion:
/// <c>AmputationSystem</c> needs two qualifying hits (the crossing hit only calls <c>SetSeverable(true)</c>)
/// while <c>DestructibleSystem</c> fires on the crossing hit itself, so wherever the gib rung is at or below
/// the amputation threshold the gib wins deterministically.
/// </para>
/// </remarks>
[TestFixture]
[TestOf(typeof(WoundDamageProjectionSystem))]
public sealed class WolfmedSpeciesSpawnTest : GameTest
{
    /// <summary>
    /// PLAN5 §6.2 T-P5-2. PROTO Q in one assertion block: an IPC is a wound host whose every limb carries the
    /// IPC profile, whose bloodstream holds oil and no chemicals at all (U16), and which spawns and deletes
    /// cleanly.
    /// </summary>
    [Test]
    public async Task IpcIsAWoundHostWithTheIpcProfileTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var bloodstream = entities.GetComponent<BloodstreamComponent>(body);

            Assert.That(entities.HasComponent<WoundHostComponent>(body), Is.True);
            Assert.That(entities.HasComponent<PainShockTargetComponent>(body), Is.True,
                "P5-D10: Wolfgate keeps PainShockTarget on BaseMobSpeciesOrganic, which MobIPC does not " +
                "inherit, so PROTO Q restates it - Onyx has it on BaseSpeciesMob, which its MobIpc parents.");

            var parts = graph.GetBodyChildren(body).ToList();
            Assert.That(parts, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                foreach (var (part, component) in parts)
                {
                    Assert.That(entities.GetComponent<WoundableComponent>(part).Profile,
                        Is.EqualTo(new ProtoId<BodyPartProfilePrototype>("IpcBodyPartProfile")),
                        $"every IPC slot takes the chassis profile, including {component.PartType}.");
                    // fractureProfile: null on WolfmedPartIpc - no IPC slot can ever fracture.
                    Assert.That(entities.System<WolfmedBodyPartSystem>().Get(part).FractureProfile, Is.Null);
                }

                // PROTO Q's Bloodstream block, field by field. Onyx's `bloodReferenceSolution: Oil 250` has no
                // Wolfgate equivalent, so it maps onto the classic bloodReagent + bloodMaxVolume pair (P5-D4).
                Assert.That(bloodstream.BloodReagent, Is.EqualTo(new ProtoId<ReagentPrototype>("Oil")));
                Assert.That(bloodstream.BloodMaxVolume, Is.EqualTo(FixedPoint2.New(250)));
                // U16: an IPC has no metabolizer (OrganIPCPump's Metabolizer block is commented out), so a
                // 250u chemical solution would be a trap. InjectableSolution is deliberately absent with it.
                Assert.That(bloodstream.ChemicalMaxVolume, Is.EqualTo(FixedPoint2.Zero));

                // P5-D6: Bloodloss, never Heat. Heat is in WoundHostComponent.LocalizedDamageTypes, so
                // bloodloss damage dealt as Heat would route to a limb, raise IpcMechanicalDamageWound
                // severity and bleed at chance 1 - a self-reinforcing leak loop.
                Assert.That(bloodstream.BloodlossDamage.DamageDict.Keys,
                    Is.EquivalentTo(new[] { new ProtoId<DamageTypePrototype>("Bloodloss") }));
                Assert.That(bloodstream.BloodlossHealDamage.DamageDict.Keys,
                    Is.EquivalentTo(new[] { new ProtoId<DamageTypePrototype>("Bloodloss") }));
            });

            entities.DeleteEntity(body);
        });

        await Pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(entities.Deleted(body), Is.True,
            "an IPC must spawn and delete without a RemCompDeferred or container assert."));
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-3 (U2(a) shipped). An IPC leaks oil, and that leak can actually hurt it — which is the
    /// entire point of the <c>SiliconWolfmed</c> container. The <c>Bloodloss</c> assertion is what catches its
    /// absence: the stock <c>Silicon</c> container has no <c>Bloodloss</c> type and
    /// <c>DamageableSystem</c> drops unsupported types silently, with no log.
    /// </summary>
    [Test]
    public async Task IpcLeaksOilAndTakesBloodlossTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var bloodstream = entities.GetComponent<BloodstreamComponent>(body);
            var blood = entities.System<BloodstreamSystem>();
            var solutions = entities.System<SharedSolutionContainerSystem>();

            Assert.That(entities.GetComponent<DamageableComponent>(body).DamageContainerID,
                Is.EqualTo("SiliconWolfmed"),
                "PROTO Q moves the IPC mob off the stock Silicon container.");

            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Slash", 30), null,
                ignoreResistances: true));

            // IpcMechanicalDamageWound bleeds at wound level with chance 1 and no minimumSeverity, and
            // WoundBleedingSystem.RefreshBody pushes the total into BloodstreamComponent.BleedAmount through
            // CirculatoryStreamSystem.SetBleedRates.
            Assert.That(bloodstream.BleedAmount, Is.GreaterThan(0f),
                "a wounded chassis must register on the body's bleed total, or the analyzer and the " +
                "tourniquet have nothing to act on.");

            // The spilled fluid is Oil, not Blood - and Oil is flammability: 2 with a FlammableTileReaction,
            // so an IPC's trail can be set alight.
            Assert.That(solutions.TryGetSolution(body, bloodstream.BloodSolutionName, out var solution, out _));
            Assert.That(solution!.Value.Comp.Solution.Contents.Select(reagent => reagent.Reagent.Prototype),
                Does.Contain("Oil"));

            var before = blood.GetBloodLevelPercentage(body);
            Assert.That(blood.TryModifyBloodLevel(body, FixedPoint2.New(-50)));
            Assert.That(blood.GetBloodLevelPercentage(body), Is.LessThan(before),
                "and the level falls as it leaks.");

            // THE assertion that catches a missing SiliconWolfmed. Bloodloss is what BloodstreamSystem.Update
            // deals below the 0.9 threshold (PROTO Q's `bloodlossDamage: {Bloodloss: 0.5}`), and what phase 3's
            // P3-D1 vital-part charge applies to the body. Dealt directly here rather than by waiting on the
            // bloodstream update tick, so the test is deterministic.
            Assert.That(entities.System<WolfmedDamageableSystem>()
                .TryChangeDamage(body, Spec("Bloodloss", 10), out _));
            Assert.That(entities.GetComponent<DamageableComponent>(body).Damage.DamageDict
                    .GetValueOrDefault(new ProtoId<DamageTypePrototype>("Bloodloss")),
                Is.EqualTo(FixedPoint2.New(10)),
                "the stock Silicon container has no Bloodloss type, so this is 0 without SiliconWolfmed - " +
                "and oil loss would be purely cosmetic (PLAN5 U2).");
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-12. PROTO S/T are not optional (PLAN5 R3): <c>WeldingHealableSystem</c> gates on the
    /// tool's <c>damageContainers</c> list, so PROTO Q's container change would otherwise silently remove the
    /// only way to repair an IPC. <c>SiliconRepairFinishedEvent</c> is <c>protected</c> inside
    /// <c>SharedWeldingHealableSystem</c> and cannot be raised from a test, so the gate is asserted directly
    /// and the healing line the handler runs after it is driven by hand.
    /// </summary>
    [Test]
    public async Task WelderStillRepairsAnIpcTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var welder = entities.SpawnEntity("Welder", map.GridCoords);
            var nanite = entities.SpawnEntity("NaniteApplicator", map.GridCoords);
            var wounds = entities.System<WoundSystem>();
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var container = entities.GetComponent<DamageableComponent>(body).DamageContainerID;

            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WeldingHealableComponent>(body), Is.True,
                    "MobIPC is welder-repairable (P5-D15 keeps it that way rather than adding Repairable).");
                Assert.That(entities.GetComponent<WeldingHealingComponent>(welder).DamageContainers,
                    Does.Contain(container), "PROTO S - without this line the welder stops repairing IPCs.");
                Assert.That(entities.GetComponent<WeldingHealingComponent>(nanite).DamageContainers,
                    Does.Contain(container), "PROTO T - same for the Mono nanite applicator.");
            });

            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Slash", 40), null,
                ignoreResistances: true));
            var wound = FindWound(entities, wounds, arm, "IpcMechanicalDamageWound");
            Assert.That(wound.Comp.Severity, Is.EqualTo(FixedPoint2.New(40)));

            // WeldingHealableSystem.OnRepairFinished's payload line, verbatim:
            //   _damageableSystem.TryChangeDamage(uid, component.Damage, true, false, origin: args.User)
            // The welder's block is Blunt/Piercing/Slash -25 each. No treatment-capability scope is opened by
            // that path, and WoundDamageRoutingSystem.CanTreatPart returns true with no scope open, so the
            // heal reaches the chassis wound for free - which is why P5-D16 does not add a Healing block.
            var repair = entities.GetComponent<WeldingHealingComponent>(welder).Damage;
            Assert.That(entities.System<WolfmedDamageableSystem>()
                .TryChangeDamage(body, repair, out _, ignoreResistances: true, interruptsDoAfters: false));

            Assert.That(wound.Comp.Severity, Is.LessThan(FixedPoint2.New(40)),
                "the welder's own damage block must reduce chassis-wound severity, not just the body total.");
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-11 (U3′(b) shipped). <c>PartIPCBase</c> is a single <c>Destructible</c> block that is
    /// first in every IPC slot's parent list, so its two rungs replace <c>MajorLimb</c>'s and
    /// <c>MinorLimb</c>'s wholesale. This documents trap 7 rather than asserting a bug: Slash severs because
    /// 130 &lt; 210, pure Blunt destroys because 190 &lt; 250.
    /// </summary>
    [Test]
    public async Task IpcLimbSeverabilityAndGibCeilingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var slashed = EntityUid.Invalid;
        var blunted = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var routing = Routing(entities);

            // (i) 140 pure Slash. The IPC arm inherits WolfmedBaseLeftArm's organic amputation set - Slash 130
            // (P5-D7: Onyx's own 270/400/600 are NOT ported, they sit on a different part chain) - and
            // PartIPCBase's Slash rung is 210, so the limb survives and AmputationSystem arms it.
            var first = entities.SpawnEntity("MobIPC", map.GridCoords);
            slashed = Part(entities, first, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(routing.TryApplyPartDamage(first, slashed, Spec("Slash", 140), null,
                ignoreResistances: true));

            // (ii) 195 pure Blunt on a fresh IPC. The Blunt rung is 190 and the Blunt amputation threshold is
            // 250, so the part is destroyed before it can ever be severed - the same contradiction organic
            // limbs already live with (PLAN5 R11, handed to the balance pass).
            var second = entities.SpawnEntity("MobIPC", map.GridCoords);
            blunted = Part(entities, second, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(routing.TryApplyPartDamage(second, blunted, Spec("Blunt", 195), null,
                ignoreResistances: true));
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.Deleted(slashed), Is.False,
                    "Slash 140 is well under PartIPCBase's Slash rung of 210 (PROTO O(c), raised from 150).");
                Assert.That(entities.GetComponent<WoundableComponent>(slashed).Severable, Is.True,
                    "140/130 >= 1, so the crossing hit arms the limb for a later finishing hit.");
                Assert.That(entities.Deleted(blunted), Is.True,
                    "Blunt 195 clears the 190 rung (PROTO O(c), raised from 110) and GibPart always QueueDels.");
            });
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-10. Regression guard for P5-D11: body damage on a wound host is the sum of every part's
    /// positive damage (<c>WoundDamageProjectionSystem.RefreshBodyDamage</c>), so <c>MobIPC</c>'s old flat
    /// Blunt 400 was reachable from routine limb damage — the same reason D22 raised organics to 1500.
    /// </summary>
    [Test]
    public async Task IpcDoesNotGibFromRoutineLimbDamageTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var routing = Routing(entities);
            var limbs = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .Where(part => part.Component.PartType is BodyPartType.Arm or BodyPartType.Leg)
                .Select(part => part.Id)
                .ToList();
            Assert.That(limbs, Has.Count.EqualTo(4));

            // 150 per limb keeps every part under its own 190 Blunt rung while putting 600 on the body - well
            // past the old 400 threshold and well short of the new 1500 one.
            foreach (var limb in limbs)
                Assert.That(routing.TryApplyPartDamage(body, limb, Spec("Blunt", 150), null,
                    ignoreResistances: true));

            Assert.That(entities.GetComponent<DamageableComponent>(body).Damage.DamageDict
                    .GetValueOrDefault(new ProtoId<DamageTypePrototype>("Blunt")),
                Is.GreaterThan(FixedPoint2.New(400)),
                "the projected body total must genuinely clear the OLD threshold, or this guard is vacuous.");
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() => Assert.That(entities.Deleted(body), Is.False,
            "PROTO Q raised MobIPC's gib threshold 400 -> 1500 precisely so routine limb damage cannot " +
            "delete the corpse (D22/P5-D11)."));
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-9 (U4(a) shipped). The <c>WoundHost</c> exclusion is lifted: protogen is a plain
    /// organic host. The organ half pins P5-D19/U12′ as a KNOWN gap rather than letting it rot — protogen
    /// organs are <c>BaseProtogenOrgan</c>-derived and carry no <c>OrganDamage</c>, so there is no organ
    /// damage, no organ destruction, no internal bleeding and none of the seven organ surgeries.
    /// </summary>
    [Test]
    public async Task ProtogenIsAWoundHostTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var hosts = new EntityUid[2];

        await server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            // Both hosts EXT 3 creates. MobProtogenDummy parents BaseSpeciesDummy and is deliberately not one.
            hosts[0] = entities.SpawnEntity("MobProtogen", map.GridCoords);
            hosts[1] = entities.SpawnEntity("MobProtogenRandom", map.GridCoords);

            Assert.Multiple(() =>
            {
                foreach (var host in hosts)
                {
                    Assert.That(entities.HasComponent<WoundHostComponent>(host), Is.True,
                        "WolfmedWoundHostExclusionSystem.ExcludedAncestors is empty as of P5-D9.");

                    var parts = graph.GetBodyChildren(host).ToList();
                    Assert.That(parts, Is.Not.Empty);
                    foreach (var (part, component) in parts)
                        Assert.That(entities.GetComponent<WoundableComponent>(part).Profile,
                            Is.EqualTo(new ProtoId<BodyPartProfilePrototype>("OrganicBodyPartProfile")),
                            $"protogen is organic, not cybernetic (U4(a)); {component.PartType} must prove it.");

                    // P5-D19 / U12′, stated rather than omitted. PartProtogen derives from BasePart and the
                    // organs from BaseProtogenOrgan, and the seven marked `parent:` edits that attach
                    // OrganDamage live only on Body/Organs/human.yml. Flip this assertion the day
                    // _Mono/Body/Organs/protogen.yml is instrumented - do not delete it.
                    foreach (var (organ, _) in graph.GetBodyOrgans(host))
                        Assert.That(entities.HasComponent<OrganDamageComponent>(organ), Is.False,
                            "protogen organs carry no OrganDamage - a recorded phase-5 gap, not an omission.");
                }
            });

            foreach (var host in hosts)
                entities.DeleteEntity(host);
        });

        // The WP9 crash class the exclusion system's own comment records: a RemCompDeferred/_deleteSet assert
        // while a host tears down.
        await Pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            foreach (var host in hosts)
                Assert.That(entities.Deleted(host), Is.True);
        }));
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-20 (U17(a) shipped). Stops "diona limbs cannot be severed" being read as "diona limbs
    /// are indestructible": <c>amputationThresholds: {}</c> disables <c>AmputationSystem</c> only, while the
    /// inherited <c>MajorLimb</c> gib rung still deletes the limb outright — so no thrown limb, no stump
    /// <c>AmputationConsequenceWound</c>, no reattachment.
    /// </summary>
    [Test]
    public async Task DionaLimbIsDestroyedNotSeveredTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var arm = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobDiona", map.GridCoords);
            arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            Assert.That(entities.System<WolfmedBodyPartSystem>().Get(arm).AmputationThresholds, Is.Empty);
            // 195 Blunt clears MajorLimb's Blunt rung of 190. WolfmedPartDiona declares no Destructible of its
            // own, so the limb keeps the trigger every organic arm and leg carries (Body/Parts/base.yml).
            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Blunt", 195), null,
                ignoreResistances: true));
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() => Assert.That(entities.Deleted(arm), Is.True,
            "an unseverable limb is still destructible - the plant profile changes amputation, not " +
            "Destructible (PLAN5 §8.1a, U17(a))."));
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-14. Enforces P5-D3 permanently and loudly. Wolfgate's trimmed
    /// <c>CirculatoryStreamSystem.SetBleedRates</c> is the only reachable stream-aware path, and a profile
    /// that named a second stream would silently zero that species' bleeding with no log at all.
    /// </summary>
    [Test]
    public async Task EveryBodyPartProfileUsesThePrimaryStreamTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var profiles = prototypes.EnumeratePrototypes<BodyPartProfilePrototype>().ToList();
            Assert.That(profiles, Has.Count.GreaterThanOrEqualTo(5),
                "phase 5 brings the shipped set to the five Onyx profiles plus any test fixtures.");

            Assert.Multiple(() =>
            {
                foreach (var profile in profiles)
                    Assert.That(profile.CirculatoryStream, Is.EqualTo(CirculatoryStreamPrototype.PrimaryStream),
                        $"{profile.ID} names a non-primary circulatory stream. Wolfgate has exactly one blood " +
                        "solution per body, so SetBleedRates would drop that species' bleed rate to zero " +
                        "silently. Onyx sets this key on none of its own five profiles either (P5-D3).");
            });
        });
    }

    private static EntityUid Part(
        IEntityManager entities,
        EntityUid body,
        BodyPartType type,
        BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static WoundDamageRoutingSystem Routing(IEntityManager entities) =>
        entities.System<WoundDamageRoutingSystem>();

    private static Entity<WoundComponent> FindWound(
        IEntityManager entities,
        WoundSystem wounds,
        EntityUid part,
        string prototype)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Single(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype));
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
