using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: Onyx's SharedBodySystem.TryDetachPart lives on WolfmedBodySystem here.
using Content.Shared.Alert;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Onyx.Wounds;

[TestFixture]
[TestOf(typeof(WoundFractureSystem))]
public sealed class WoundFractureTest : GameTest
{
    // WOLFGATE: Shitmed body graph instead of Onyx's Nubody `InitialBody`; Chest → Torso (D9); the armour has no
    // `coverage` in Wolfgate, so it protects every part — the leg reduction the test measures is unchanged.
    // WOLFGATE (P2-D21, WP10-1): `WoundFractureBody` gains a `left hand` slot and `- type: Hands`.
    // `FractureEffectSystem.TryGetUsedHandSymmetry` bails on `!TryComp(body, out HandsComponent?)`, and a hand
    // only exists once an enabled `BodyPartType.Hand` part is attached (HandsSystem.TryAddHand), so without
    // this `GetDurationMultiplier` is a flat 1f that masquerades as "fractures don't affect manipulation".
    // Exactly ONE hand here: `GetDurationMultiplier(body)` resolves the ACTIVE hand, and `AddHand` makes
    // whichever hand attaches first active, so a two-handed fixture would depend on body-graph slot order.
    // The symmetric `WoundFractureHandsBody` below exists for the `used`-item branch (T-FRACT-HANDS).
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WoundFractureBodyGraph
  name: ""wound fracture body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - left arm
      - left leg
    left arm:
      part: LeftArmHuman
      connections:
      - left hand
    left hand:
      part: LeftHandHuman
    left leg:
      part: LeftLegHuman

- type: entity
  id: WoundFractureBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WoundFractureBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MovementSpeedModifier
  - type: Hands
  # WOLFGATE (WP10-6b): AlertsSystem.ShowAlert silently returns without AlertsComponent, so the BrokenBones
  # assertions in T-FRACT-ALERT would read false for a reason unrelated to FractureAlertSystem.
  - type: Alerts
  - type: WoundHost

- type: body
  id: WoundFractureHandsBodyGraph
  name: ""wound fracture hands body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - left arm
      - right arm
    left arm:
      part: LeftArmHuman
      connections:
      - left hand
    left hand:
      part: LeftHandHuman
    right arm:
      part: RightArmHuman
      connections:
      - right hand
    right hand:
      part: RightHandHuman

- type: entity
  id: WoundFractureHandsBody
  components:
  - type: Body
    prototype: WoundFractureHandsBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MovementSpeedModifier
  - type: Hands
  - type: WoundHost

- type: entity
  id: WoundFractureArmor
  components:
  - type: Clothing
    slots: [outerClothing]
  - type: Armor
    modifiers:
      coefficients:
        Blunt: 0.5

# WOLFGATE (WP10-6b): T-FRACT-HANDS needs something to hold; FractureEffectSystem.TryGetUsedHandSymmetry's
# `used` branch goes through SharedHandsSystem.IsHolding, which only resolves a hand for a real item.
- type: entity
  id: WoundFractureHeldItem
  components:
  - type: Item
";

    [Test]
    public async Task GradeBoundariesAreDeterministicTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            ProtoId<FractureProfilePrototype> profileId = "OrganicFractureProfile";
            var profile = prototypes.Index(profileId);
            // WOLFGATE: Onyx's literals (15/30/50/75) are stale against its own pinned prototype. The vendored
            // OrganicFractureProfile is byte-identical to Onyx's and declares 20/35/50/60, so the boundaries
            // below are the profile's, not a Wolfgate behaviour change. Re-check on any Onyx re-sync.
            Assert.Multiple(() =>
            {
                Assert.That(WoundFractureSystem.GetGrade(profile, 19), Is.EqualTo(FractureGrade.None));
                Assert.That(WoundFractureSystem.GetGrade(profile, 20), Is.EqualTo(FractureGrade.Hairline));
                Assert.That(WoundFractureSystem.GetGrade(profile, 34), Is.EqualTo(FractureGrade.Hairline));
                Assert.That(WoundFractureSystem.GetGrade(profile, 35), Is.EqualTo(FractureGrade.Simple));
                Assert.That(WoundFractureSystem.GetGrade(profile, 49), Is.EqualTo(FractureGrade.Simple));
                Assert.That(WoundFractureSystem.GetGrade(profile, 50), Is.EqualTo(FractureGrade.Displaced));
                Assert.That(WoundFractureSystem.GetGrade(profile, 59), Is.EqualTo(FractureGrade.Displaced));
                Assert.That(WoundFractureSystem.GetGrade(profile, 60), Is.EqualTo(FractureGrade.Comminuted));
            });
        });
    }

    [Test]
    public async Task PostArmorHitAndTreatmentPreconditionsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFractureBody", map.GridCoords);
            var armor = entityManager.SpawnEntity("WoundFractureArmor", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var inventory = entityManager.System<InventorySystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var fractures = entityManager.System<WoundFractureSystem>();
            var leg = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Leg).Id;

            Assert.That(inventory.TryEquip(body, armor, "outerClothing"));
            Assert.That(routing.TryApplyPartDamage(body, leg, Spec(150)));
            var fracture = fractures.GetFracture(leg).Value;
            Assert.That(fracture.Comp1.Severity, Is.EqualTo(FixedPoint2.New(75)));
            Assert.That(fracture.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted));
            Assert.That(fractures.TryMend(fracture.Owner));
            Assert.That(fractures.GetFracture(leg), Is.Null);
        });
    }

    /// <summary>
    /// T-FRACT-EFFECTS (PLAN2 §6.2): Onyx's EffectsRefreshOnTreatmentHealingAndDetachTest, ported. A fractured
    /// leg slows the mob, a fractured arm slows hand work, mending clears the manipulation penalty and
    /// detaching the leg clears the movement one — every refresh path in FractureEffectSystem.
    /// </summary>
    [Test]
    public async Task EffectsRefreshOnTreatmentHealingAndDetachTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFractureBody", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var wfBody = entityManager.System<WolfmedBodySystem>(); // WOLFGATE: Onyx's SharedBodySystem.TryDetachPart lives here (§2.7).
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var fractures = entityManager.System<WoundFractureSystem>();
            var manipulation = entityManager.System<FractureEffectSystem>(); // class name, not the file name.
            var hands = entityManager.System<SharedHandsSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var leg = parts.Single(part => part.Component.PartType == BodyPartType.Leg).Id;
            var arm = parts.Single(part => part.Component.PartType == BodyPartType.Arm).Id;

            // WOLFGATE (P2-D21): guard first. FractureEffectSystem.OnGetMultiplier returns before touching a
            // single part unless TryGetUsedHandSymmetry resolves a hand, so without the T-FIXTURE hand every
            // manipulation assertion below would measure a flat 1f and read as "fractures do nothing".
            Assert.That(hands.GetActiveHand((body, entityManager.GetComponent<HandsComponent>(body))), Is.Not.Null,
                "no active hand on WoundFractureBody: the T-FIXTURE `left hand` slot or `- type: Hands` is missing, and every manipulation assertion below is vacuous.");
            Assert.That(manipulation.GetDurationMultiplier(body), Is.EqualTo(1f).Within(0.001f),
                "an undamaged body must not modify do-after duration.");

            // WOLFGATE (P2-D23): 75 >= the Comminuted threshold (60), whose creationChance is 1, so the
            // fracture is created deterministically. A 35 hit would roll Simple's 0.25 and fail 3 runs in 4.
            Assert.That(routing.TryApplyPartDamage(body, leg, Spec(75)));
            Assert.That(fractures.GetFracture(leg)!.Value.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted));

            // WOLFGATE (P2-D16, measured): Onyx's literal is 0.4f, stale against its own shipped profile.
            // Comminuted leg -> movementModifier 0; WoundHostComponent.PartEffectScales[Leg] = 0.5
            // (WoundDamageComponents.cs:87); TreatmentEffectScales[None] = 1 (wounds.yml). OnRefreshSpeed does
            // ModifySpeed(1 - (1 - 0) * 0.5 * 1) = 0.5. The fixture has exactly one mobility part, so that is
            // the whole product.
            Assert.That(entityManager.GetComponent<MovementSpeedModifierComponent>(body).WalkSpeedModifier,
                Is.EqualTo(0.5f).Within(0.001f));

            Assert.That(routing.TryApplyPartDamage(body, arm, Spec(75)));
            Assert.That(fractures.GetFracture(arm)!.Value.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted));

            // WOLFGATE (P2-D16 + DECISIONS §8.2-1, measured): Onyx's literal is 2f — written against the C#
            // defaults in WoundPrototype.cs, not against its own YAML, which shipped manipulationModifier
            // values below 1 (i.e. a shattered arm made do-afters FASTER). DECISIONS §8.2-1 restored the C#
            // defaults 1.1/1.25/1.5/2.0 in wounds.yml, so the correct expectation is 2.0 and PLAN2 P2-D16's
            // interim prediction of 0.75 is itself stale. Derivation: Comminuted left arm ->
            // manipulationModifier 2.0, Arm absent from PartEffectScales -> partScale 1, treatment None -> 1,
            // so 1 + (2.0 - 1) * 1 * 1 = 2.0; the undamaged left hand falls through GetEffect to
            // BodyPartFunctionalitySystem.GetState, which returns Functional while
            // wounds.body_part_functionality_enabled is false (P2-3), giving 1 + (1 - 1) * 0.75 * 1 = 1.
            // Product 2.0.
            Assert.That(manipulation.GetDurationMultiplier(body), Is.EqualTo(2f).Within(0.001f));

            // removeWoundWhenMended: true -> the wound is gone, so GetEffect takes the Functional fallback.
            Assert.That(fractures.TryMend(fractures.GetFracture(arm)!.Value.Owner));
            Assert.That(fractures.GetFracture(arm), Is.Null);
            Assert.That(manipulation.GetDurationMultiplier(body), Is.EqualTo(1f).Within(0.001f));

            // The leg is still Comminuted; detaching it removes the only mobility part, so the refresh raised
            // from OnPartChanged (OrganGotRemovedEvent, re-raised by WolfmedBodyPartLifecycleSystem) must
            // restore full speed.
            Assert.That(wfBody.TryDetachPart(leg));
            Assert.That(entityManager.GetComponent<MovementSpeedModifierComponent>(body).WalkSpeedModifier,
                Is.EqualTo(1f).Within(0.001f));
        });
    }

    /// <summary>
    /// T-FRACT-HANDS (PLAN2 §6.2): new coverage for the one method the port rewrote
    /// (FractureEffectSystem.TryGetUsedHandSymmetry, P2-D2). The held item's hand decides which side's arm is
    /// consulted — a fractured left arm must not slow work done with the right hand.
    /// </summary>
    [Test]
    public async Task FractureManipulationUsesHeldHandSymmetryTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFractureHandsBody", map.GridCoords);
            var leftItem = entityManager.SpawnEntity("WoundFractureHeldItem", map.GridCoords);
            var rightItem = entityManager.SpawnEntity("WoundFractureHeldItem", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var fractures = entityManager.System<WoundFractureSystem>();
            var manipulation = entityManager.System<FractureEffectSystem>();
            var hands = entityManager.System<SharedHandsSystem>();
            var handsComp = entityManager.GetComponent<HandsComponent>(body);

            // WOLFGATE (P2-D21): the hands must actually exist, or IsHolding never resolves and every
            // multiplier below is the "no hand found" 1f rather than a measurement.
            var leftHand = hands.EnumerateHands(body, handsComp).Single(hand => hand.Location == HandLocation.Left);
            var rightHand = hands.EnumerateHands(body, handsComp).Single(hand => hand.Location == HandLocation.Right);
            Assert.That(hands.TryPickup(body, leftItem, leftHand, checkActionBlocker: false, animate: false, handsComp: handsComp));
            Assert.That(hands.TryPickup(body, rightItem, rightHand, checkActionBlocker: false, animate: false, handsComp: handsComp));

            var leftArm = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Arm &&
                                part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            Assert.That(routing.TryApplyPartDamage(body, leftArm, Spec(75))); // P2-D23: Comminuted, creationChance 1.
            Assert.That(fractures.GetFracture(leftArm)!.Value.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted));

            // WOLFGATE: OnGetMultiplier filters GetBodyChildren by ManipulationParts AND
            // bodyPart.Symmetry == symmetry, so the right-hand item only ever sees the undamaged right arm and
            // right hand: 1 * 1 = 1. The left-hand item sees the Comminuted left arm (2.0, see
            // EffectsRefreshOnTreatmentHealingAndDetachTest for the derivation) and the intact left hand (1).
            // This is the assertion that proves the P2-D2 rewrite kept Onyx's `used`-item semantics; it never
            // consults the active hand, so it is independent of body-graph slot order.
            Assert.That(manipulation.GetDurationMultiplier(body, rightItem), Is.EqualTo(1f).Within(0.001f));
            Assert.That(manipulation.GetDurationMultiplier(body, leftItem), Is.EqualTo(2f).Within(0.001f));
        });
    }

    /// <summary>
    /// T-FRACT-ALERT (PLAN2 §6.2): the BrokenBones alert appears with a qualifying fracture and clears on
    /// treatment. Only reachable with FractureEffectSystem present — FractureAlertSystem.Refresh has no other
    /// caller (P2-D19).
    /// </summary>
    [Test]
    public async Task FractureAlertTracksGradeAndTreatmentTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFractureBody", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var fractures = entityManager.System<WoundFractureSystem>();
            var alerts = entityManager.System<AlertsSystem>();
            var leg = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Leg).Id;

            Assert.That(alerts.IsShowingAlert(body, BrokenBones), Is.False);

            // WOLFGATE (P2-D23): 60 is exactly the Comminuted threshold and its creationChance is 1, so this
            // is the only fully deterministic way to put a fracture on the leg. severityMultiplier: 1 makes
            // severity == damage == 60.
            Assert.That(routing.TryApplyPartDamage(body, leg, Spec(60)));
            var fracture = fractures.GetFracture(leg)!.Value;
            Assert.That(fracture.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted));
            Assert.That(fracture.Comp1.Severity, Is.EqualTo(FixedPoint2.New(60)));
            Assert.That(alerts.IsShowingAlert(body, BrokenBones), Is.True);

            // Mending clears it twice over: alertHiddenTreatments: [Mended] hides it, and
            // removeWoundWhenMended: true removes the wound outright.
            Assert.That(fractures.TryMend(fracture.Owner));
            Assert.That(fractures.GetFracture(leg), Is.Null);
            Assert.That(alerts.IsShowingAlert(body, BrokenBones), Is.False);
        });
    }

    /// <summary>
    /// T-FRACT-ALERT-NEG (PLAN2 §6.2): alertMinimumGrade: Simple actually gates the alert. Reached by healing
    /// a deterministic Comminuted fracture down, never by a sub-Comminuted hit (P2-D23).
    /// </summary>
    [Test]
    public async Task FractureAlertRespectsMinimumGradeTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFractureBody", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var fractures = entityManager.System<WoundFractureSystem>();
            var wounds = entityManager.System<WoundSystem>();
            var alerts = entityManager.System<AlertsSystem>();
            var leg = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Leg).Id;

            Assert.That(routing.TryApplyPartDamage(body, leg, Spec(60)));
            var fracture = fractures.GetFracture(leg)!.Value;
            Assert.That(alerts.IsShowingAlert(body, BrokenBones), Is.True);

            // WOLFGATE (P2-D23): WoundFractureSystem.OnWoundChanged re-grades with no random roll, so
            // ChangeSeverity is a deterministic grade dial. 60 - 25 = 35 = Simple's threshold, still >=
            // alertMinimumGrade.
            Assert.That(wounds.ChangeSeverity(fracture.Owner, FixedPoint2.New(-25)));
            Assert.That(fracture.Comp2.Grade, Is.EqualTo(FractureGrade.Simple));
            Assert.That(alerts.IsShowingAlert(body, BrokenBones), Is.True);

            // 35 - 10 = 25 -> Hairline (threshold 20), below alertMinimumGrade: Simple. The wound is still
            // there; it is the grade gate, not the wound's existence, that this asserts.
            Assert.That(wounds.ChangeSeverity(fracture.Owner, FixedPoint2.New(-10)));
            Assert.That(fracture.Comp2.Grade, Is.EqualTo(FractureGrade.Hairline));
            Assert.That(fractures.GetFracture(leg), Is.Not.Null);
            Assert.That(alerts.IsShowingAlert(body, BrokenBones), Is.False);
        });
    }

    private static readonly ProtoId<AlertPrototype> BrokenBones = "BrokenBones";

    private static DamageSpecifier Spec(int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>("Blunt")] = FixedPoint2.New(amount) },
    };
}
