#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Content.Shared.Traits.Assorted;
using Robust.Shared.GameObjects;
using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// CONSC: a wound host's mob state is decided by consciousness, not by a damage total. Pain, blood, legs and
/// airloss each own a threshold, painkillers discount pain by tier, and Downed restricts a player to
/// themselves.
/// </summary>
/// <remarks>
/// The fixture carries no <c>PainShockTarget</c> on purpose: the pain shock's 30 s adrenaline window
/// multiplies every pain reading by 0.7 the moment raw pain reaches 130, which would make every number in
/// the pain tests a function of when the shock fired. The blood tests use a real MobHuman, which has one,
/// and keep pain at zero so it never arms.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedConsciousnessSystem))]
public sealed class WolfmedConsciousnessTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WolfmedConscBodyGraph
  name: ""wolfmed consciousness body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - head
      - left leg
      - right leg
    head:
      part: HeadHuman
    left leg:
      part: LeftLegHuman
    right leg:
      part: RightLegHuman

- type: entity
  id: WolfmedConscBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedConscBodyGraph
    requiredLegs: 2
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Alerts
  - type: MovementSpeedModifier
  - type: StandingState
  - type: WoundHost

- type: entity
  id: WolfmedConscPlainMob
  parent: InventoryBase
  components:
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      50: Critical
      100: Dead
";

    /// <summary>
    /// The premise of the whole package: on a wound host a damage total decides nothing. A mob without
    /// WoundHost is the control and still crosses its thresholds exactly as it always did.
    /// </summary>
    [Test]
    public async Task DamageTotalsNeverCritAWoundHostTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var mobState = entities.System<MobStateSystem>();
            var damage = entities.System<DamageableSystem>();

            // Numb, so pain contributes nothing and damage is the only thing left that could do it.
            entities.EnsureComponent<PainNumbnessComponent>(body);

            damage.TryChangeDamage(body, Spec("Blunt", 250), ignoreResistances: true);
            Assert.That(mobState.IsAlive(body), Is.True,
                "250 Blunt crit a wound host: the MobThresholdSystem gate is not holding.");
            Assert.That(mobState.IsCritical(body), Is.False);
            Assert.That(mobState.IsDead(body), Is.False);

            // Past the 600 body damage cap, which is itself well past the old dead threshold.
            damage.TryChangeDamage(body, Spec("Blunt", 350), ignoreResistances: true);
            Assert.That(mobState.IsAlive(body), Is.True);
            Assert.That(mobState.IsDead(body), Is.False);

            // The control: no WoundHost, so nothing changed for it.
            var plain = entities.SpawnEntity("WolfmedConscPlainMob", map.GridCoords);
            damage.TryChangeDamage(plain, Spec("Blunt", 60), ignoreResistances: true);
            Assert.That(entities.System<MobStateSystem>().IsCritical(plain), Is.True,
                "a mob without WoundHost stopped crossing its own thresholds.");

            damage.TryChangeDamage(plain, Spec("Blunt", 60), ignoreResistances: true);
            Assert.That(mobState.IsDead(plain), Is.True);
        });
    }

    /// <summary>
    /// Pain past 0.70 of the soft cap puts a body on the floor, and the hysteresis keeps it there until the
    /// pain has dropped a tenth below the value that downed it.
    /// </summary>
    [Test]
    public async Task PainDownsAndThenReleasesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var pain = entities.System<PainSystem>();
            var consciousness = entities.System<WolfmedConsciousnessSystem>();
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);

            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));

            // 0.70 x the 135 soft cap is 94.5. 95 is the first whole number past it.
            pain.SetPain(body, FixedPoint2.New(95));
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed));
            Assert.That(entities.HasComponent<WolfmedDownedComponent>(body), Is.True);
            Assert.That(entities.GetComponent<MobStateComponent>(body).CurrentState,
                Is.EqualTo(MobState.Alive), "Downed is conscious: it is not Critical.");

            // Hysteresis: 0.9 x 94.5 = 85.05, so 86 is still on the floor.
            pain.SetPain(body, FixedPoint2.New(86));
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed),
                "the body stood up inside the hysteresis band.");

            // 0.60 of the cap is 81, which is 0.857 of the Downed threshold: under the band, so up.
            pain.SetPain(body, FixedPoint2.New(81));
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));
            Assert.That(entities.HasComponent<WolfmedDownedComponent>(body), Is.False);
        });
    }

    /// <summary>What Downed costs: everything past the body's own reach.</summary>
    [Test]
    public async Task DownedReachesOnlyItselfTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var pen = entities.SpawnEntity("EmergencyMedipen", map.GridCoords);
            var airlock = entities.SpawnEntity("Airlock", map.GridCoords);
            var blocker = entities.System<ActionBlockerSystem>();
            var downed = entities.System<WolfmedDownedSystem>();

            Assert.That(blocker.CanInteract(body, airlock), Is.True);
            Assert.That(blocker.CanAttack(body), Is.True);

            entities.System<PainSystem>().SetPain(body, FixedPoint2.New(95));
            Assert.That(entities.HasComponent<WolfmedDownedComponent>(body), Is.True);

            // Themselves and what they are carrying, and nothing else.
            Assert.That(blocker.CanInteract(body, body), Is.True, "a Downed body cannot reach itself.");
            Assert.That(blocker.CanInteract(body, null), Is.True);
            Assert.That(blocker.CanUseHeldEntity(body, pen), Is.True,
                "a Downed body cannot use what is already in its hands.");
            Assert.That(downed.IsSelfOrCarried(body, body), Is.True);
            Assert.That(downed.IsSelfOrCarried(body, pen), Is.False);

            Assert.That(blocker.CanInteract(body, airlock), Is.False, "a Downed body opened a door.");
            Assert.That(blocker.CanAttack(body), Is.False, "a Downed body swung at something.");
            Assert.That(blocker.CanThrow(body, pen), Is.False);
        });
    }

    /// <summary>
    /// Past the unconscious point the body goes Critical, a weak painkiller can never lift that, and a
    /// strong one can because pain was the only thing holding it there.
    /// </summary>
    [Test]
    public async Task PainCritIsLiftedByStrongPainkillersOnlyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var pain = entities.System<PainSystem>();
            var relief = entities.System<WolfmedPainReliefSystem>();
            var consciousness = entities.System<WolfmedConsciousnessSystem>();
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;

            // The unconscious test reads pain BEFORE the soft clamp, which is the sum of the parts: the
            // body's own value is clamped to 135 and could never say more than 1.0 of the cap.
            pain.SetPain(torso, FixedPoint2.New(100));
            pain.SetPain(head, FixedPoint2.New(100));
            Assert.That(consciousness.GetUncappedPain(body), Is.EqualTo(200f).Within(0.01f));
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious));
            Assert.That(entities.System<MobStateSystem>().IsCritical(body), Is.True);

            // Weak: 1.25 x 135 = 168.75, and 200 - 22 is still past it. Weak never lifts unconsciousness
            // however much it discounts, because it is not counted against this threshold at all.
            relief.AddDose(body, "weak", WolfmedPainReliefTier.Weak, 22f, TimeSpan.FromSeconds(30), 0f);
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious),
                "a weak painkiller lifted unconsciousness.");

            // Strong: 200 - 70 = 130, under 168.75, and the effective pain of 135 - 92 is under the Downed
            // threshold too, so the patient is back on their feet.
            relief.AddDose(body, "strong", WolfmedPainReliefTier.Strong, 70f, TimeSpan.FromSeconds(30), 0f);
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.Not.EqualTo(WolfmedConsciousness.Unconscious),
                "a strong painkiller could not lift a crit that was pain and nothing else.");
            Assert.That(entities.System<MobStateSystem>().IsCritical(body), Is.False);
        });
    }

    /// <summary>
    /// Blood is its own input: it downs and crits a body with no pain at all, and no painkiller of any tier
    /// touches it. That is the whole of the owner's "up until you have no blood" rule.
    /// </summary>
    [Test]
    public async Task BloodLossDownsCritsAndIgnoresPainkillersTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var bloodstream = entities.System<BloodstreamSystem>();
            var consciousness = entities.System<WolfmedConsciousnessSystem>();
            var relief = entities.System<WolfmedPainReliefSystem>();
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);

            void SetBlood(float fraction)
            {
                var current = bloodstream.GetBloodLevelPercentage(body);
                bloodstream.TryModifyBloodLevel(body, FixedPoint2.New((fraction - current) * 300f));
                consciousness.OnBloodLevelChanged(body, bloodstream.GetBloodLevelPercentage(body));
            }

            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));

            // 0.50 is 1.25 of the way to the 0.60 Downed threshold and 0.91 of the way to the 0.45 one.
            SetBlood(0.5f);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed));

            // 0.40 is past 0.45.
            SetBlood(0.4f);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious));

            // Not one tier of painkiller touches blood.
            relief.AddDose(body, "strong", WolfmedPainReliefTier.Strong, 200f, TimeSpan.FromSeconds(30), 0f);
            relief.AddDose(body, "pen", WolfmedPainReliefTier.Emergency, 0f, TimeSpan.FromSeconds(30), 0f);
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious),
                "a painkiller lifted a body that had run out of blood.");

            // 0.62 is 0.95 of the way to the Downed threshold: inside the hysteresis band, still down.
            SetBlood(0.62f);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed));

            SetBlood(0.75f);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));
        });
    }

    /// <summary>Both legs out of action puts a body on the floor, and one working leg picks it back up.</summary>
    [Test]
    public async Task DisabledLegsDownTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var consciousness = entities.System<WolfmedConsciousnessSystem>();
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);
            var legs = graph.GetBodyChildren(body)
                .Where(part => part.Component.PartType == BodyPartType.Leg)
                .Select(part => part.Id)
                .ToList();

            Assert.That(legs, Has.Count.EqualTo(2));

            foreach (var leg in legs)
                entities.EnsureComponent<BodyPartFunctionalityComponent>(leg).State =
                    BodyPartFunctionalityState.Disabled;

            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed));

            entities.GetComponent<BodyPartFunctionalityComponent>(legs[0]).State =
                BodyPartFunctionalityState.Functional;
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));
        });
    }

    /// <summary>
    /// The emergency pen buys a window on your feet however bad the pain is, and then charges for it: pain
    /// up by the crash multiplier, and Downed for the crash's duration.
    /// </summary>
    [Test]
    public async Task EmergencyPenLiftsThenCrashesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var pain = entities.System<PainSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;

            pain.SetPain(torso, FixedPoint2.New(100));
            pain.SetPain(head, FixedPoint2.New(100));
            Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(body).State,
                Is.EqualTo(WolfmedConsciousness.Unconscious));

            // The reagent's own window is 30 s; this is the same mechanism on a timer a test can wait out.
            entities.System<WolfmedPainReliefSystem>()
                .AddDose(body, "pen", WolfmedPainReliefTier.Emergency, 0f, TimeSpan.FromSeconds(1), 0f);
            entities.System<WolfmedConsciousnessSystem>().Refresh(body);
            Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(body).State,
                Is.EqualTo(WolfmedConsciousness.Up),
                "the emergency pen did not lift a body that pain alone had put out.");
        });

        // Past the window and into the crash, but not past the crash's own 10 s.
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);
            var relief = entities.GetComponent<WolfmedPainReliefComponent>(body);
            Assert.That(relief.EmergencyEnds, Is.Null);
            Assert.That(relief.CrashEnds, Is.Not.Null, "the pen's window ended without a crash.");
            Assert.That(comp.State, Is.Not.EqualTo(WolfmedConsciousness.Up),
                "the crash left the body on its feet.");

            // Pain x1.3 on every part that had any: 100 each becomes 130 each, less whatever the natural
            // recovery of 1/9 a second has taken off since the crash landed.
            var pain = entities.System<PainSystem>();
            var graph = entities.System<SharedBodySystem>();
            var torso = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            Assert.That(pain.GetRawPain(torso).Float(), Is.EqualTo(130f).Within(1f));
        });
    }

    /// <summary>
    /// Too much of a sedating painkiller depresses breathing. That is the lethal end of an overdose today,
    /// and the seam BRAIN replaces with oxygenation.
    /// </summary>
    [Test]
    public async Task SedationOverdoseTakesAirTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            entities.System<WolfmedPainReliefSystem>()
                .AddDose(body, "opiate", WolfmedPainReliefTier.Strong, 10f, TimeSpan.FromSeconds(30), 0.5f);
        });

        // 0.5 sedation a second, past the 0.6 threshold inside two.
        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            var relief = entities.GetComponent<WolfmedPainReliefComponent>(body);
            Assert.That(relief.Sedation, Is.GreaterThan(relief.SedationAirlossThreshold));

            var damage = entities.GetComponent<DamageableComponent>(body);
            Assert.That(damage.DamagePerGroup.TryGetValue("Airloss", out var airloss), Is.True);
            Assert.That(airloss, Is.GreaterThan(FixedPoint2.Zero),
                "a sedation overdose stopped costing the patient air.");
        });
    }

    /// <summary>
    /// Suffocation still reaches Critical with the damage thresholds gated off: airloss is read against the
    /// threshold it used to cross and pushed in as an external pressure, which is BRAIN's seam.
    /// </summary>
    [Test]
    public async Task AirlossStillReachesCriticalTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);

            // The fixture's crit threshold is 100, so 80 Asphyxiation is 0.8 of a pressure: Downed, because
            // 0.8 is past the 0.7 share that puts a body on the floor.
            entities.System<DamageableSystem>()
                .TryChangeDamage(body, Spec("Asphyxiation", 80), ignoreResistances: true);
            Assert.That(comp.Pressures.ContainsKey(WolfmedConsciousnessSystem.AirlossPressure), Is.True);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed));

            entities.System<DamageableSystem>()
                .TryChangeDamage(body, Spec("Asphyxiation", 40), ignoreResistances: true);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious),
                "suffocation stopped reaching Critical once the thresholds were gated.");

            // No painkiller touches air either.
            entities.System<WolfmedPainReliefSystem>()
                .AddDose(body, "pen", WolfmedPainReliefTier.Emergency, 0f, TimeSpan.FromSeconds(30), 0f);
            entities.System<WolfmedConsciousnessSystem>().Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious));
        });
    }

    /// <summary>Rejuvenate is the admin's undo, and it has to undo all of this too.</summary>
    [Test]
    public async Task RejuvenateClearsConsciousnessTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            entities.System<DamageableSystem>()
                .TryChangeDamage(body, Spec("Asphyxiation", 150), ignoreResistances: true);
            entities.System<WolfmedPainReliefSystem>()
                .AddDose(body, "opiate", WolfmedPainReliefTier.Strong, 40f, TimeSpan.FromSeconds(60), 0.2f);

            Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(body).State,
                Is.EqualTo(WolfmedConsciousness.Unconscious));

            entities.EventBus.RaiseLocalEvent(body, new RejuvenateEvent());

            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));
            Assert.That(comp.Pressures, Is.Empty);
            Assert.That(entities.HasComponent<WolfmedDownedComponent>(body), Is.False);
            Assert.That(entities.HasComponent<WolfmedPainReliefComponent>(body), Is.False);
            Assert.That(entities.System<MobStateSystem>().IsAlive(body), Is.True);
        });

        // The re-evaluation is deferred by a tick so every other rejuvenate handler has run first.
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(body).State,
                Is.EqualTo(WolfmedConsciousness.Up));
            Assert.That(entities.System<MobStateSystem>().IsAlive(body), Is.True);
        });
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
