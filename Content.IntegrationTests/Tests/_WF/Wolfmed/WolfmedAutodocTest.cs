#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power;
using Content.Shared._WF.Wolfmed.Reagents;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The autodoc pod. Every procedure here runs through the real Shitmed step machinery with the pod as the
/// performer, so what these tests measure is the same wound state a surgeon's hands would have left.
/// </summary>
/// <remarks>
/// The test pod drops its step time to 0.02 s so a whole procedure fits in a handful of ticks, keeps
/// <c>needsPower: false</c> so <see cref="PowerReceiverSystem.IsPowered"/> is true without an APC, and sets
/// <c>malfunctionChance: 0</c> so no assertion depends on a roll. The slip test turns the roll back on with
/// <see cref="AutodocComponent.ForceMalfunction"/>.
/// </remarks>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class WolfmedAutodocTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedTestAutodoc
  parent: MachineAutodoc
  suffix: test
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: WolfmedTestOpiateBeaker
  parent: Beaker
  suffix: opiate
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 50
        reagents:
        - ReagentId: WolfmedOpiate
          Quantity: 50

- type: entity
  id: WolfmedTestPlasmaBeaker
  parent: Beaker
  suffix: plasma
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 50
        reagents:
        - ReagentId: Plasma
          Quantity: 50
";

    /// <summary>A queued fracture repair runs end to end and leaves the bone mended, with no tool loose in the world.</summary>
    [Test]
    public async Task PodMendsAFractureThroughTheRealStepsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid leg = default;
        Entity<AutodocComponent> pod = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 60);
            leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);

            var fractures = entities.System<WoundFractureSystem>();
            Assert.That(fractures.GetFracture(leg), Is.Not.Null, "the fixture needs a fracture to mend.");

            pod = Pod(entities, map);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        // ApcPowerReceiver.Powered is written by the power net's own tick, so a pod spawned this instant is
        // not powered yet and would refuse to start.
        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "SurgeryMendFracture", TargetBodyPart.LeftLeg), Is.True,
                "mending a fracture is in the base library.");
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(240);

        await server.WaitAssertion(() =>
        {
            var fractures = entities.System<WoundFractureSystem>();
            Assert.Multiple(() =>
            {
                // A mended fracture either reads Mended or has left the part entirely, depending on whether
                // the mend healed the wound out; either way the bone is no longer broken.
                var fracture = fractures.GetFracture(leg);
                Assert.That(fracture == null || fracture.Value.Comp2.Treatment == FractureTreatment.Mended, Is.True,
                    "the pod drove set bone and mend fracture, so the bone is no longer broken.");
                Assert.That(pod.Comp.Queue, Is.Empty, $"and the queue emptied (state {pod.Comp.State}, step {pod.Comp.CurrentStep}).");
                Assert.That(pod.Comp.State, Is.EqualTo(AutodocState.Complete));

                // Every tool is still inside the pod: nothing was dropped on the floor to be picked up.
                var containers = entities.System<SharedContainerSystem>();
                Assert.That(containers.TryGetContainer(pod, AutodocComponent.ToolContainerId, out var tools), Is.True);
                Assert.That(tools!.ContainedEntities, Is.Not.Empty);
                foreach (var tool in tools.ContainedEntities)
                    Assert.That(containers.IsEntityInContainer(tool), Is.True, "no tool leaked into the world.");
            });
        });
    }

    /// <summary>
    /// A limb attach reaches the insert step with nothing to insert, opens the tray, refuses the wrong item and
    /// completes once the right arm is in it.
    /// </summary>
    [Test]
    public async Task RequirementFlowWaitsForTheLimbAndRefusesTheWrongOneTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        EntityUid arm = default;
        Entity<AutodocComponent> pod = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            entities.System<Content.Shared._WF.Wolfmed.Compat.WolfmedBodySystem>().TryDetachPart(arm);

            pod = Pod(entities, map);
            slots.TryInsert(pod.Owner, AutodocComponent.DiskSlotId,
                entities.SpawnEntity("AutodocProgramDiskLimb", map.GridCoords), null);

            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "SurgeryAttachLeftArm", TargetBodyPart.Torso), Is.True,
                "the limb disk is in the slot.");
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var slots = entities.System<ItemSlotsSystem>();

            Assert.That(pod.Comp.State, Is.EqualTo(AutodocState.Waiting),
                "the insert step has no limb, so the pod is waiting with its tray open.");
            Assert.That(pod.Comp.Pending?.Kind, Is.EqualTo(AutodocRequirementKind.Part));
            Assert.That(autodoc.Matches(pod.Comp.Pending, entities.SpawnEntity("Scalpel", map.GridCoords)), Is.False,
                "a scalpel is not a left arm.");
            Assert.That(autodoc.Matches(pod.Comp.Pending, arm), Is.True);
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.TraySlotId, arm, null), Is.True,
                "the tray unlocked when the pod started waiting.");
        });

        await Pair.RunTicksSync(240);

        await server.WaitAssertion(() =>
        {
            var parts = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .Where(part => part.Component.PartType == BodyPartType.Arm &&
                               part.Component.Symmetry == BodyPartSymmetry.Left);

            Assert.That(parts.Any(), Is.True, "the arm is back on the body.");
            Assert.That(pod.Comp.State, Is.Not.EqualTo(AutodocState.Waiting));
        });
    }

    /// <summary>A transplant is refused without its disk and offered with it.</summary>
    [Test]
    public async Task ProgramDiskGatesTransplantTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            var pod = Pod(entities, map);

            Assert.Multiple(() =>
            {
                Assert.That(autodoc.IsKnown(pod, "SurgeryRemoveHeart"), Is.False, "a bare pod cannot transplant.");
                Assert.That(autodoc.IsKnown(pod, "SurgeryMendFracture"), Is.True, "but it can mend a bone.");
            });

            slots.TryInsert(pod.Owner, AutodocComponent.DiskSlotId,
                entities.SpawnEntity("AutodocProgramDiskTransplant", map.GridCoords), null);

            Assert.That(autodoc.IsKnown(pod, "SurgeryRemoveHeart"), Is.True, "the disk unlocks it.");
            Assert.That(autodoc.IsKnown(pod, "SurgeryInsertBrain"), Is.False, "and only it: brains need neuro.");
        });
    }

    /// <summary>A loaded opiate beaker reaches the patient's bloodstream; an empty reservoir does not.</summary>
    [Test]
    public async Task AnaestheticIsDrawnFromTheReservoirTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var slots = entities.System<ItemSlotsSystem>();

            var dry = Pod(entities, map);
            var patient = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(autodoc.PushReagent(dry, patient, AutodocReagentRole.Anaesthetic, 15f), Is.False,
                "no beaker, no anaesthetic.");

            var wet = Pod(entities, map);
            slots.TryInsert(wet.Owner, AutodocComponent.ReservoirSlotIds[0],
                entities.SpawnEntity("WolfmedTestOpiateBeaker", map.GridCoords), null);

            Assert.That(autodoc.PushReagent(wet, patient, AutodocReagentRole.Anaesthetic, 15f), Is.True,
                "the opiate is on the administrable list.");

            // Straight into the chemical solution: waiting on metabolism would make this a timing test.
            var bloodstream = entities.GetComponent<Content.Server.Body.Components.BloodstreamComponent>(patient);
            Assert.That(entities.System<SharedSolutionContainerSystem>()
                .TryGetSolution(patient, bloodstream.ChemicalSolutionName, out _, out var chemicals), Is.True);
            Assert.That(chemicals!.GetTotalPrototypeQuantity("WolfmedOpiate") > FixedPoint2.Zero, Is.True,
                "the opiate is in the patient, not in the beaker.");
        });
    }

    /// <summary>Plasma in a beaker is not medicine and the pod will not touch it.</summary>
    [Test]
    public async Task PlasmaBeakerIsIgnoredTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            var pod = Pod(entities, map);
            var patient = entities.SpawnEntity("MobHuman", map.GridCoords);

            slots.TryInsert(pod.Owner, AutodocComponent.ReservoirSlotIds[0],
                entities.SpawnEntity("WolfmedTestPlasmaBeaker", map.GridCoords), null);

            Assert.Multiple(() =>
            {
                Assert.That(autodoc.PushReagent(pod, patient, AutodocReagentRole.Anaesthetic, 15f), Is.False);
                Assert.That(autodoc.PushReagent(pod, patient, AutodocReagentRole.Fluid, 15f), Is.False,
                    "plasma is on no list at all.");
            });
        });
    }

    /// <summary>Downed is allowed to crawl into a pod; unconscious is not allowed to do anything.</summary>
    [Test]
    public async Task DownedMayClimbInUnconsciousMayNotTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var blocker = entities.System<ActionBlockerSystem>();
            var pod = Pod(entities, map);
            var downed = entities.SpawnEntity("MobHuman", map.GridCoords);
            var locker = entities.SpawnEntity("ClosetSteelBase", map.GridCoords);

            entities.EnsureComponent<WolfmedDownedComponent>(downed);

            Assert.Multiple(() =>
            {
                Assert.That(blocker.CanInteract(downed, pod.Owner), Is.True,
                    "the pod is the one thing off the body a Downed player may still reach.");
                Assert.That(blocker.CanInteract(downed, locker), Is.False,
                    "and nothing else became reachable with it.");
            });

            var out_ = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.System<MobStateSystem>().ChangeMobState(out_, Content.Shared.Mobs.MobState.Critical);
            Assert.That(blocker.CanInteract(out_, pod.Owner), Is.False, "an unconscious body cannot climb in.");
        });
    }

    /// <summary>Power out stops the pod mid-procedure; power back resumes it.</summary>
    [Test]
    public async Task PowerLossPausesAndRestoreResumesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 60);

            pod = Pod(entities, map);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "SurgeryMendFracture", TargetBodyPart.LeftLeg), Is.True);
            Assert.That(autodoc.TryStart(pod, null), Is.True);

            var lost = new PowerChangedEvent(false, 0f);
            entities.EventBus.RaiseLocalEvent(pod.Owner, ref lost);

            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp.State, Is.EqualTo(AutodocState.Paused), "the pod stops where it stands.");
                Assert.That(pod.Comp.PowerPaused, Is.True);
            });
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(pod.Comp.State, Is.EqualTo(AutodocState.Paused), "and stays stopped.");

            var restored = new PowerChangedEvent(true, 1000f);
            entities.EventBus.RaiseLocalEvent(pod.Owner, ref restored);

            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp.PowerPaused, Is.False);
                Assert.That(pod.Comp.State, Is.EqualTo(AutodocState.Step), "and picks the step back up.");
            });
        });
    }

    /// <summary>An emagged pod locks its lid and takes a limb off, and prying is the way out.</summary>
    [Test]
    public async Task EmagAmputatesAndLocksTheLidTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            pod = Pod(entities, map);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);

            // What EmagSystem.DoEmagEffect does: raise the event, then leave EmaggedComponent behind. The
            // pod reads the component, so the test has to leave it too.
            var emag = new GotEmaggedEvent(entities.SpawnEntity("MobHuman", map.GridCoords), EmagType.Interaction);
            entities.EventBus.RaiseLocalEvent(pod.Owner, ref emag);
            entities.EnsureComponent<EmaggedComponent>(pod.Owner).EmagType = EmagType.Interaction;

            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp.Locked, Is.True, "the lid locks the moment it is emagged.");
                Assert.That(autodoc.TryEject(pod), Is.False, "and the eject verb cannot open it.");
            });
        });

        await Pair.RunTicksSync(600);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var limbs = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .Count(part => part.Component.PartType is BodyPartType.Arm or BodyPartType.Leg);

            Assert.That(limbs, Is.LessThan(4), "it has taken a limb off, or is part way through one.");
            Assert.That(autodoc.TryEject(pod, force: true), Is.True, "prying forces the lid.");
        });
    }

    /// <summary>A slip opens one shallow bleeding cut on the part being worked on, and nothing worse.</summary>
    [Test]
    public async Task SlipOpensOneSmallWoundTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid leg = default;
        Entity<AutodocComponent> slipPod = default;
        var before = 0;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var wounds = entities.System<WoundSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 60);
            leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            before = wounds.GetWounds(leg).Count();

            slipPod = Pod(entities, map);
            Assert.That(autodoc.TryInsert(slipPod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(slipPod, "SurgeryMendFracture", TargetBodyPart.LeftLeg), Is.True);
            slipPod.Comp.ForceMalfunction = true;
            Assert.That(autodoc.TryStart(slipPod, null), Is.True);
        });

        await Pair.RunTicksSync(240);

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var slash = wounds.GetWounds(leg).Count(wound => wound.Comp.Prototype == "SlashWound");
            Assert.That(slash, Is.GreaterThan(0), "the forced slip cut the leg.");
            Assert.That(wounds.GetWounds(leg).Count(), Is.LessThanOrEqualTo(before + 3),
                "and did nothing else: a slip is one wound, not a mangling.");
        });
    }

    /// <summary>Every voice event has at least one line and every line has a file on disk.</summary>
    [Test]
    public async Task EveryVoiceLineHasAFileTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var voice = protos.Index<AutodocVoicePrototype>("WolfmedAutodocVoiceSam");

            Assert.Multiple(() =>
            {
                foreach (var value in Enum.GetValues<AutodocVoiceEvent>())
                {
                    Assert.That(voice.Events.ContainsKey(value), Is.True, $"{value} has no line.");
                    Assert.That(voice.Events[value], Is.Not.Empty, $"{value} has an empty variant list.");

                    foreach (var id in voice.Events[value])
                    {
                        Assert.That(voice.Lines.ContainsKey(id), Is.True, $"{value} names a missing line {id}.");
                        var line = voice.Lines[id];
                        var path = ((Robust.Shared.Audio.SoundPathSpecifier) line.Sound).Path;
                        Assert.That(resources.ContentFileExists(path), Is.True, $"{id} has no ogg at {path}.");
                        Assert.That(Robust.Shared.IoC.IoCManager.Resolve<Robust.Shared.Localization.ILocalizationManager>()
                            .TryGetString(line.Message, out _), Is.True, $"{id} has no transcript.");
                    }
                }
            });
        });
    }

    /// <summary>
    /// BRAIN: with the cardiac module fitted the pod runs the same defibrillation rule a medic's paddles do,
    /// on its own, on an occupant whose heart has stopped.
    /// </summary>
    [Test]
    public async Task DefibModuleRevivesAnArrestedOccupantTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var life = entities.System<Content.Server._WF.Wolfmed.Life.WolfmedLifeSystem>();
            var revival = entities.System<Content.Server._WF.Wolfmed.Life.WolfmedRevivalSystem>();
            var pod = Pod(entities, map);
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(autodoc.TryInsert(pod, body), Is.True);
            Assert.That(life.StartArrest(body, "test"), Is.True);

            // Without the module the pod says so and nothing happens.
            Assert.That(autodoc.TryDefibrillateOccupant(pod, body), Is.False);
            Assert.That(life.InArrest(body), Is.True);

            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.ModuleSlotId,
                entities.SpawnEntity("AutodocDefibModule", map.GridCoords), null), Is.True);
            Assert.That(autodoc.HasDefibModule(pod), Is.True);

            revival.ForcedRoll = 0f;
            Assert.That(autodoc.TryDefibrillateOccupant(pod, body), Is.True);
            revival.ForcedRoll = null;

            Assert.Multiple(() =>
            {
                Assert.That(life.InArrest(body), Is.False, "the pod's shock did not restart the heart.");
                Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.False);
            });
        });
    }

    /// <summary>
    /// Every line is short enough for the job it does: a step announcement has to be over before the step
    /// it announces, and nothing may hold the queue up for long.
    /// </summary>
    [Test]
    public async Task VoiceLinesStayWithinTheirDurationBoundsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var voice = protos.Index<AutodocVoicePrototype>("WolfmedAutodocVoiceSam");

            Assert.Multiple(() =>
            {
                foreach (var (id, line) in voice.Lines)
                {
                    var seconds = autodoc.GetLineLength(line).TotalSeconds;
                    Assert.That(seconds, Is.GreaterThan(0.1), $"{id} is silent.");

                    switch (line.Priority)
                    {
                        case AutodocVoicePriority.Step:
                            Assert.That(seconds, Is.LessThanOrEqualTo(1.2), $"{id} is too long for a step.");
                            break;
                        case AutodocVoicePriority.Urgent:
                            Assert.That(seconds, Is.LessThanOrEqualTo(3.0), $"{id} is too long to interrupt with.");
                            break;
                        case AutodocVoicePriority.Info:
                            Assert.That(seconds, Is.LessThanOrEqualTo(4.0), $"{id} holds the queue up.");
                            break;
                    }
                }
            });
        });
    }

    /// <summary>Three step announcements at once: one is spoken and the rest are dropped, never stacked.</summary>
    [Test]
    public async Task StepLinesNeverOverlapTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var pod = Pod(entities, map);
            Silence(pod);

            autodoc.Speak(pod, AutodocVoiceEvent.StepIncision);
            autodoc.Speak(pod, AutodocVoiceEvent.StepSaw);
            autodoc.Speak(pod, AutodocVoiceEvent.StepSuture);

            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp.VoiceSpoken, Is.EqualTo(1), "only the first step line was spoken.");
                Assert.That(pod.Comp.VoiceQueue, Is.Empty, "and the other two were dropped, not queued.");
                Assert.That(autodoc.IsSpeaking(pod), Is.True);
            });
        });
    }

    /// <summary>An urgent line cuts a playing info line off mid-word instead of waiting behind it.</summary>
    [Test]
    public async Task UrgentLineInterruptsAPlayingLineTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid greeting = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            pod = Pod(entities, map);
            Silence(pod);

            autodoc.Speak(pod, AutodocVoiceEvent.Greeting);
            Assert.That(pod.Comp.VoiceStream, Is.Not.Null, "the greeting is playing.");
            greeting = pod.Comp.VoiceStream!.Value;

            autodoc.Speak(pod, AutodocVoiceEvent.Critical);

            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp.VoiceSpoken, Is.EqualTo(2), "the urgent line started at once.");
                Assert.That(pod.Comp.VoiceStream, Is.Not.EqualTo(greeting), "on a stream of its own.");
                Assert.That(pod.Comp.VoiceQueue, Is.Empty, "and nothing was left waiting.");
            });
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(greeting), Is.True, "the greeting's stream was stopped, not left to finish.");
        });
    }

    /// <summary>
    /// Open, closed and operating each map to their own art, and the occupant is drawn either way: on top of
    /// the bed with the lid open, under the lid with it closed.
    /// </summary>
    [Test]
    public async Task VisualStateFollowsTheLidTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 60);
            pod = Pod(entities, map);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var appearance = entities.System<SharedAppearanceSystem>();
            var containers = entities.System<SharedContainerSystem>();

            Assert.That(autodoc.TryInsert(pod, body), Is.True);
            Assert.That(State(appearance, pod), Is.EqualTo(AutodocVisualState.Closed),
                "an occupied pod draws its lid.");

            Assert.That(containers.TryGetContainer(pod, AutodocComponent.BodyContainerId, out var held), Is.True);
            Assert.That(held!.ShowContents, Is.True, "the occupant is drawn, not hidden inside the machine.");

            Assert.That(autodoc.TryQueue(pod, "SurgeryMendFracture", TargetBodyPart.LeftLeg), Is.True);
            Assert.That(autodoc.TryStart(pod, null), Is.True);
            Assert.That(State(appearance, pod), Is.EqualTo(AutodocVisualState.Operating),
                "and the animated lid while it works.");

            Assert.That(autodoc.TryEject(pod, force: true), Is.True);
            Assert.That(State(appearance, pod), Is.EqualTo(AutodocVisualState.Open),
                "an empty pod is open, with nothing over the bed.");
        });
    }

    private static AutodocVisualState State(SharedAppearanceSystem appearance, Entity<AutodocComponent> pod)
    {
        appearance.TryGetData<AutodocVisualState>(pod.Owner, AutodocVisuals.State, out var state);
        return state;
    }

    /// <summary>Forgets whatever the pod said while it was starting up, so a queue assertion starts from silence.</summary>
    private static void Silence(Entity<AutodocComponent> pod)
    {
        pod.Comp.VoiceQueue.Clear();
        pod.Comp.VoiceBusyUntil = TimeSpan.Zero;
        pod.Comp.VoiceSpoken = 0;
        pod.Comp.VoiceStream = null;
    }

    private static Entity<AutodocComponent> Pod(IEntityManager entities, TestMapData map)
    {
        var pod = entities.SpawnEntity("WolfmedTestAutodoc", map.GridCoords);
        return (pod, entities.GetComponent<AutodocComponent>(pod));
    }

    private static void Blunt(IEntityManager entities, EntityUid body, TargetBodyPart target, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec(amount), origin: null, targetPart: target);
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type, BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static DamageSpecifier Spec(int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>("Blunt")] = FixedPoint2.New(amount) },
    };
}
