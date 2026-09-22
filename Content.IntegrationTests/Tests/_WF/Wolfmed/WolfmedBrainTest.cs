#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Systems;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Content.Shared.Traits.Assorted;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// BRAIN: the heart and the oxygenation clock. Nothing here is a damage total, nothing here is permanent,
/// and death is ordinary <see cref="MobState.Dead"/> with a way back out of it.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedLifeSystem))]
public sealed class WolfmedBrainTest : GameTest
{
    /// <summary>Blood at or under 30% stops the heart, and only a defibrillator starts it again.</summary>
    [Test]
    public async Task BloodLossStopsTheHeartTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var mobState = entities.System<MobStateSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            life.Tick(body, 1f);
            Assert.That(life.InArrest(body), Is.False, "a healthy body started in arrest.");

            Bleed(entities, body, 0.25f);
            life.Tick(body, 1f);

            Assert.Multiple(() =>
            {
                Assert.That(life.InArrest(body), Is.True, "a body at a quarter of its blood kept a pulse.");
                Assert.That(mobState.IsCritical(body), Is.True);
                Assert.That(life.BleedFactor(body), Is.EqualTo(0.25f), "bleeding kept its pressure with no pulse.");
                Assert.That(entities.HasComponent<UnrevivableComponent>(body), Is.False,
                    "Wolfmed marked a body unrevivable.");
            });

            // Blood back, then the paddles. Blood alone is not enough; the heart has to be restarted.
            entities.System<BloodstreamSystem>().TryModifyBloodLevel(body, FixedPoint2.New(400));
            life.Tick(body, 1f);
            Assert.That(life.InArrest(body), Is.True, "restoring the blood restarted the heart by itself.");

            var revival = entities.System<WolfmedRevivalSystem>();
            revival.ForcedRoll = 0f;
            Assert.That(revival.TryDefibrillate(body, out _), Is.True);
            revival.ForcedRoll = null;

            Assert.Multiple(() =>
            {
                Assert.That(life.InArrest(body), Is.False);
                Assert.That(mobState.IsDead(body), Is.False);
            });
        });
    }

    /// <summary>A heart out of the chest is arrest. Putting it back with blood behind it is a way out.</summary>
    [Test]
    public async Task HeartRemovalStopsTheHeartTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var graph = entities.System<SharedBodySystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            var heart = Organ<HeartComponent>(entities, body);
            var torso = graph.GetBodyChildrenOfType(body, BodyPartType.Torso).First().Id;
            Assert.That(graph.RemoveOrgan(heart), Is.True);
            Assert.That(life.InArrest(body), Is.True, "a body with its heart in a tray kept a pulse.");
            Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.False,
                "a missing heart killed the patient outright instead of stopping it.");

            Assert.That(graph.InsertOrgan(torso, heart, "heart"), Is.True);
            life.Tick(body, 1f);
            Assert.That(life.InArrest(body), Is.False,
                "a heart back in the chest with full blood did not restart.");
        });
    }

    /// <summary>
    /// The clock. Untreated the brain runs dry and the organ dies; under CPR the same wall time leaves it
    /// alive; cold slows it further still.
    /// </summary>
    [Test]
    public async Task ArrestClockRunsOutTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid untreated = default;

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            untreated = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(life.StartArrest(untreated, "test"), Is.True);

            // 120 s of a stopped heart is the whole tank.
            Run(life, untreated, 120);
            Assert.That(life.GetOxygenation(untreated), Is.EqualTo(0f).Within(0.01f));
            Assert.That(life.GetBrainActivity(untreated), Is.LessThan(1f),
                "an unoxygenated brain took no damage.");
            Assert.That(entities.System<MobStateSystem>().IsDead(untreated), Is.False,
                "the brain was destroyed inside the first two minutes.");

            // Under CPR the same wall time barely moves it.
            var cpr = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(life.StartArrest(cpr, "test"), Is.True);
            Assert.That(entities.System<WolfmedRevivalSystem>().StartCpr(cpr, TimeSpan.FromMinutes(30)), Is.True);
            Run(life, cpr, 120);
            Assert.Multiple(() =>
            {
                Assert.That(life.GetOxygenation(cpr), Is.GreaterThan(0.6f), "CPR stopped buying time.");
                Assert.That(life.GetBrainActivity(cpr), Is.EqualTo(1f), "CPR let the brain take damage.");
            });

            // Cold is even better: a tenth of the drain below 20 C.
            var cold = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.GetComponent<TemperatureComponent>(cold).CurrentTemperature = 280f;
            Assert.That(life.StartArrest(cold, "test"), Is.True);
            Run(life, cold, 120);
            Assert.That(life.GetOxygenation(cold), Is.GreaterThan(life.GetOxygenation(cpr)),
                "a cold body did not keep its brain longer than a warm one under CPR.");
        });

        await server.WaitAssertion(() =>
        {
            // The rest of the way: the organ reaches zero and the brain is dead.
            var life = entities.System<WolfmedLifeSystem>();
            Run(life, untreated, 200);
        });

        // OrganHealthSystem turns a destroyed brain into MobState.Dead on its own tick.
        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.System<MobStateSystem>().IsDead(untreated), Is.True,
                    "the oxygenation clock never killed anybody.");
                Assert.That(entities.HasComponent<UnrevivableComponent>(untreated), Is.False,
                    "brain death marked the body unrevivable.");
            });
        });
    }

    /// <summary>
    /// Brain death is not the end of it. The paddles refuse a destroyed brain, the surgery puts it back, and
    /// then the same paddles work.
    /// </summary>
    [Test]
    public async Task BrainRepairMakesADeadBodyDefibrillatableTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var brain = life.GetBrainOrgan(body);
            Assert.That(brain, Is.Not.Null);
            entities.System<OrganHealthSystem>().SetHealth(brain!.Value, FixedPoint2.Zero);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var revival = entities.System<WolfmedRevivalSystem>();
            var mobState = entities.System<MobStateSystem>();
            Assert.That(mobState.IsDead(body), Is.True, "a destroyed brain did not kill the patient.");

            revival.ForcedRoll = 0f;
            Assert.That(revival.TryDefibrillate(body, out var refused), Is.False,
                "the paddles restarted a body with no brain activity.");
            Assert.That(refused, Is.EqualTo("wolfmed-defib-brain-dead"));

            // The surgery is the only thing that raises organ health.
            life.RepairBrain(body);
            Assert.That(life.GetBrainActivity(body), Is.EqualTo(1f));
            Assert.That(entities.HasComponent<WolfmedBrainTraumaComponent>(body), Is.True,
                "a repaired brain came back with no trauma.");

            Assert.That(revival.TryDefibrillate(body, out var line), Is.True);
            Assert.That(line, Is.EqualTo("wolfmed-defib-success"));
            revival.ForcedRoll = null;

            Assert.Multiple(() =>
            {
                Assert.That(mobState.IsDead(body), Is.False, "the revived body stayed dead.");
                Assert.That(mobState.IsCritical(body), Is.True, "the revived body woke straight up.");
                Assert.That(life.InArrest(body), Is.False);
            });
        });
    }

    /// <summary>Blood under the defibrillator's floor is a refusal, whatever the brain says.</summary>
    [Test]
    public async Task DefibrillatorNeedsBloodAndABrainTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var revival = entities.System<WolfmedRevivalSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Bleed(entities, body, 0.25f);
            life.Tick(body, 1f);

            revival.ForcedRoll = 0f;
            Assert.That(revival.TryDefibrillate(body, out var line), Is.False);
            Assert.That(line, Is.EqualTo("wolfmed-defib-no-blood"));
            revival.ForcedRoll = null;
        });
    }

    /// <summary>A head torn off takes the brain with it, and that is death the moment it lands.</summary>
    [Test]
    public async Task BrainLeavingTheBodyIsDeathTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            var mobState = entities.System<MobStateSystem>();

            var debrained = entities.SpawnEntity("MobHuman", map.GridCoords);
            var brain = entities.System<WolfmedLifeSystem>().GetBrainOrgan(debrained);
            Assert.That(brain, Is.Not.Null);
            Assert.That(graph.RemoveOrgan(brain!.Value), Is.True);
            Assert.That(mobState.IsDead(debrained), Is.True, "a body with its brain in a jar kept living.");
            Assert.That(entities.HasComponent<UnrevivableComponent>(debrained), Is.False);
        });
    }

    /// <summary>
    /// The canary. A wound host with no brain at all is a test fixture, not a corpse: nothing polls for a
    /// missing brain, so it lives exactly as long as anything else does.
    /// </summary>
    [Test]
    public async Task BrainlessWoundHostKeepsLivingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("WolfmedBrainlessBody", map.GridCoords);
            var life = entities.System<WolfmedLifeSystem>();
            for (var second = 0; second < 30; second++)
                life.Tick(body, 10f);

            Assert.Multiple(() =>
            {
                Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.False,
                    "a wound host with no brain was killed by the life system.");
                Assert.That(life.InArrest(body), Is.False, "a brainless body went into cardiac arrest.");
            });
        });

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.False,
                "a wound host with no brain died on the real tick.");
        });
    }

    /// <summary>Suffocation drains the brain, and putting the air back lets it fill again.</summary>
    [Test]
    public async Task SuffocationDrainsAndRecoversTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var damage = entities.System<DamageableSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            damage.TryChangeDamage(body, Spec("Asphyxiation", 200), ignoreResistances: true);
            Run(life, body, 60);
            var starved = life.GetOxygenation(body);
            Assert.That(starved, Is.LessThan(0.8f), "suffocation cost the brain nothing.");

            damage.TryChangeDamage(body, Spec("Asphyxiation", -200), ignoreResistances: true);
            Run(life, body, 60);
            Assert.That(life.GetOxygenation(body), Is.GreaterThan(starved),
                "the brain never refilled once the patient was breathing again.");
        });
    }

    /// <summary>Rejuvenate undoes an arrest, the clock and the organ damage behind it.</summary>
    [Test]
    public async Task RejuvenateClearsTheClockTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(life.StartArrest(body, "test"), Is.True);
            Run(life, body, 200);

            entities.EventBus.RaiseLocalEvent(body, new RejuvenateEvent());

            Assert.Multiple(() =>
            {
                Assert.That(life.InArrest(body), Is.False);
                Assert.That(life.GetOxygenation(body), Is.EqualTo(1f));
                Assert.That(life.GetBrainActivity(body), Is.EqualTo(1f));
            });
        });
    }

    /// <summary>
    /// A machine body has no clock. Losing the pump shuts it down, which is not death; losing the positronic
    /// brain is.
    /// </summary>
    [Test]
    public async Task MechanicalShutdownAndDeathTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid ipc = default;

        await server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            var shutdown = entities.System<WolfmedShutdownSystem>();
            var life = entities.System<WolfmedLifeSystem>();
            ipc = entities.SpawnEntity("MobIPC", map.GridCoords);

            Assert.That(shutdown.IsMechanical(ipc), Is.True, "the IPC fixture is not mechanical.");
            Assert.That(life.GetBrain(ipc), Is.Null, "a chassis is running an oxygenation clock.");

            // The cell going flat is the other half of it, and it is not death either.
            entities.EventBus.RaiseLocalEvent(ipc, new Content.Server._EinsteinEngines.Silicon.Death
                .SiliconChargeDeathEvent(ipc, null, ipc));
            Assert.That(shutdown.IsShutDown(ipc), Is.True, "a chassis with a flat cell kept running.");
            entities.EventBus.RaiseLocalEvent(ipc, new Content.Server._EinsteinEngines.Silicon.Death
                .SiliconChargeAliveEvent(ipc, null, ipc));
            Assert.That(shutdown.IsShutDown(ipc), Is.False, "power came back and the chassis stayed down.");

            var pump = Organ<HeartComponent>(entities, ipc);
            Assert.That(graph.RemoveOrgan(pump), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(shutdown.IsShutDown(ipc), Is.True, "a chassis with no pump kept running.");
                Assert.That(entities.System<MobStateSystem>().IsDead(ipc), Is.False,
                    "a missing pump killed the chassis instead of shutting it down.");
                Assert.That(life.InArrest(ipc), Is.False, "a chassis went into cardiac arrest.");
            });
        });

        await server.WaitAssertion(() =>
        {
            // The positronic brain is a brain: destroying it is death, on the same organ path.
            var life = entities.System<WolfmedLifeSystem>();
            var brain = life.GetBrainOrgan(ipc);
            Assert.That(brain, Is.Not.Null, "the positronic brain carries no organ health.");
            entities.System<OrganHealthSystem>().SetHealth(brain!.Value, FixedPoint2.Zero);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<MobStateSystem>().IsDead(ipc), Is.True,
                "a destroyed positronic brain did not kill the chassis.");
        });
    }

    private static void Run(WolfmedLifeSystem life, EntityUid body, int seconds)
    {
        for (var second = 0; second < seconds; second++)
            life.Tick(body, 1f);
    }

    private static void Bleed(IEntityManager entities, EntityUid body, float target)
    {
        // BloodstreamComponent is read-only from a test assembly, so the volume is walked down instead of
        // being solved for: ten units a step is two dozen steps on a human.
        var bloodstream = entities.System<BloodstreamSystem>();
        for (var step = 0; step < 200 && bloodstream.GetBloodLevelPercentage(body) > target; step++)
            bloodstream.TryModifyBloodLevel(body, FixedPoint2.New(-10));
    }

    private static EntityUid Organ<T>(IEntityManager entities, EntityUid body) where T : IComponent
    {
        foreach (var (organ, _) in entities.System<SharedBodySystem>().GetBodyOrgans(body))
        {
            if (entities.HasComponent<T>(organ))
                return organ;
        }

        Assert.Fail($"the fixture has no {typeof(T).Name}.");
        return default;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };

    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WolfmedBrainlessGraph
  name: ""wolfmed brainless body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - left leg
      - right leg
    left leg:
      part: LeftLegHuman
    right leg:
      part: RightLegHuman

# The canary from PassiveDamageMechanismStillRoutesIfReenabledTest, in one prototype: a wound host with no
# head, no brain and no heart. Nothing may kill it on a timer.
- type: entity
  id: WolfmedBrainlessBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedBrainlessGraph
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
";
}
