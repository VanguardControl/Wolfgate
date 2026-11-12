using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared._WF.Xenobiology.Systems;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Reports from the 2026-11-12 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1112Test
{
    /// <summary>Report 800: a slime feeds on what its acid hurts and lets go of a medibot instead of feeding forever.</summary>
    [Test]
    public async Task SlimeLetsGoOfWhatItCannotHurt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var latch = entMan.System<SlimeLatchSystem>();

        EntityUid human = default, bot = default, onHuman = default, onBot = default;
        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            bot = entMan.SpawnEntity("MobMedibot", map.GridCoords);
            var xeno = entMan.System<XenobiologySystem>();
            var a = xeno.SpawnSlimeAt(map.GridCoords, "MobSlimeXenobioBaby", "GreyMutation")!.Value;
            var b = xeno.SpawnSlimeAt(map.GridCoords, "MobSlimeXenobioBaby", "GreyMutation")!.Value;
            (onHuman, onBot) = (a.Owner, b.Owner);
            latch.Latch(a, human);
            latch.Latch(b, bot);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<SlimeComponent>(onHuman).LatchedTarget, Is.EqualTo(human),
                "A slime let go of a body it was feeding on.");
            Assert.That(entMan.GetComponent<DamageableComponent>(human).TotalDamage.Float(), Is.GreaterThan(0f),
                "A latched slime didn't hurt the body it fed on.");
            Assert.That(entMan.GetComponent<SlimeComponent>(onBot).LatchedTarget, Is.Null,
                "A slime kept feeding on a medibot its acid can't hurt.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 839: a xeno can't open a folding barricade door; a person still can.</summary>
    [Test]
    public async Task XenoCannotOpenBarricadeDoor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var doors = entMan.System<SharedDoorSystem>();
            var door = entMan.SpawnEntity("CMBarricadeMetalDoor", map.GridCoords);
            if (!entMan.GetComponent<TransformComponent>(door).Anchored)
                entMan.System<SharedTransformSystem>().AnchorEntity(door);
            var xeno = entMan.SpawnEntity("CMXenoDrone", map.GridCoords);
            var person = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var comp = entMan.GetComponent<DoorComponent>(door);

            Assert.That(comp.State, Is.EqualTo(DoorState.Closed));
            Assert.That(doors.CanOpen(door, comp, xeno, quiet: true), Is.False, "A xeno could open a barricade door.");
            Assert.That(doors.CanOpen(door, comp, person, quiet: true), Is.True, "A person couldn't open a barricade door.");
        });
        await pair.CleanReturnAsync();
    }
}

/// <summary>Report 815: a construction step that takes a tagged part uses one from a stack, not the whole stack.</summary>
public sealed class WFFeedbackBatch1112ConstructionTest : InteractionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: constructionGraph
  id: WFTestStackInsertGraph
  start: start
  graph:
  - node: start
    edges:
    - to: done
      steps:
      - tag: CapacitorStockPart
        name: capacitor
  - node: done

- type: entity
  id: WFTestStackInsert
  components:
  - type: Construction
    graph: WFTestStackInsertGraph
    node: start
";

    [Test]
    public async Task TagStepTakesOneFromAStack()
    {
        await SpawnTarget("WFTestStackInsert");
        await InteractUsing("CapacitorStockPart", 3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Hands.ActiveHandEntity, Is.Not.Null, "The construction step took the whole stack.");
            Assert.That(SEntMan.GetComponent<StackComponent>(Hands.ActiveHandEntity!.Value).Count, Is.EqualTo(2),
                "The construction step didn't take exactly one part.");
        });
    }
}
