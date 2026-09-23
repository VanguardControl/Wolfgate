#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Server.Body.Components;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// M1a (plan §9.1), report mode: every round-start species answers what disables it, what kills it and what
/// restores it. The checks run for every species and the ones that fail are written to the test output; the
/// test asserts only that they are exactly <see cref="KnownGaps"/>. A new gap fails, and so does a fixed
/// species still on the list, so the list only shrinks. M4 deletes the list and makes the test strict.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedLifeSystem))]
public sealed class WolfmedSpeciesConformanceTest : GameTest
{
    /// <summary>
    /// The known gaps as of M1a, "Species: check". The plan's §9.2 groups: A′ (lungs without data), B (heart
    /// without data), C (no brain clock), D (not wound hosts), and the heartless species of C′.
    /// </summary>
    private static readonly string[] KnownGaps =
    [
        // A′ (plan §9.2): lungs without Wolfmed data.
        "Feroxi: lungs lack WolfmedOrgan",
        "Goblin: lungs lack WolfmedOrgan",
        "Harpy: lungs lack WolfmedOrgan",
        "Hydrakin: lungs lack WolfmedOrgan",
        // B: hearts without Wolfmed data (and, for some, lungs too); the heart can never be destroyed by trauma.
        "Arachnid: heart lacks WolfmedOrgan",
        "Arachnid: lungs lack WolfmedOrgan",
        "Canine: heart lacks WolfmedOrgan",
        "Felionoid: heart lacks WolfmedOrgan",
        "Moth: heart lacks WolfmedOrgan",
        "ProtoThaven: heart lacks WolfmedOrgan",
        "ProtoThaven: lungs lack WolfmedOrgan",
        "Reptilian: heart lacks WolfmedOrgan",
        "Rodentia: heart lacks WolfmedOrgan",
        "Tajaran: heart lacks WolfmedOrgan",
        "Vulpkanin: heart lacks WolfmedOrgan",
        // C and C′: no brain clock, so no arrest, and brain removal does not kill; Diona and the slimes have no heart.
        "Diona: 29% blood does not arrest",
        "Diona: brain lacks WolfmedBrain and WolfmedOrgan",
        "Diona: brain removal does not kill",
        "Diona: lungs lack WolfmedOrgan",
        "Diona: no heart",
        "ProtoArachnid: 29% blood does not arrest",
        "ProtoArachnid: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoArachnid: brain removal does not kill",
        "ProtoArachnid: heart lacks WolfmedOrgan",
        "ProtoArachnid: lungs lack WolfmedOrgan",
        "ProtoAvali: 29% blood does not arrest",
        "ProtoAvali: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoAvali: brain removal does not kill",
        "ProtoAvali: heart lacks WolfmedOrgan",
        "ProtoAvali: lungs lack WolfmedOrgan",
        "ProtoDawi: 29% blood does not arrest",
        "ProtoDawi: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoDawi: brain removal does not kill",
        "ProtoDawi: heart lacks WolfmedOrgan",
        "ProtoDawi: lungs lack WolfmedOrgan",
        "ProtoDionae: 29% blood does not arrest",
        "ProtoDionae: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoDionae: brain removal does not kill",
        "ProtoDionae: lungs lack WolfmedOrgan",
        "ProtoDionae: no heart",
        "ProtoFeline: 29% blood does not arrest",
        "ProtoFeline: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoFeline: brain removal does not kill",
        "ProtoFeline: heart lacks WolfmedOrgan",
        "ProtoFeline: lungs lack WolfmedOrgan",
        "ProtoHumie: 29% blood does not arrest",
        "ProtoHumie: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoHumie: brain removal does not kill",
        "ProtoHumie: heart lacks WolfmedOrgan",
        "ProtoHumie: lungs lack WolfmedOrgan",
        "ProtoMoth: 29% blood does not arrest",
        "ProtoMoth: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoMoth: brain removal does not kill",
        "ProtoMoth: heart lacks WolfmedOrgan",
        "ProtoMoth: lungs lack WolfmedOrgan",
        "ProtoReptile: 29% blood does not arrest",
        "ProtoReptile: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoReptile: brain removal does not kill",
        "ProtoReptile: heart lacks WolfmedOrgan",
        "ProtoReptile: lungs lack WolfmedOrgan",
        "ProtoResomi: 29% blood does not arrest",
        "ProtoResomi: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoResomi: brain removal does not kill",
        "ProtoResomi: heart lacks WolfmedOrgan",
        "ProtoResomi: lungs lack WolfmedOrgan",
        "ProtoSlimePerson: 29% blood does not arrest",
        "ProtoSlimePerson: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoSlimePerson: brain removal does not kill",
        "ProtoSlimePerson: lungs lack WolfmedOrgan",
        "ProtoSlimePerson: no heart",
        "ProtoVox: 29% blood does not arrest",
        "ProtoVox: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoVox: brain removal does not kill",
        "ProtoVox: heart lacks WolfmedOrgan",
        "ProtoVox: lungs lack WolfmedOrgan",
        "ProtoVulp: 29% blood does not arrest",
        "ProtoVulp: brain lacks WolfmedBrain and WolfmedOrgan",
        "ProtoVulp: brain removal does not kill",
        "ProtoVulp: heart lacks WolfmedOrgan",
        "ProtoVulp: lungs lack WolfmedOrgan",
        "Protogen: 29% blood does not arrest",
        "Protogen: brain lacks WolfmedBrain and WolfmedOrgan",
        "Protogen: brain removal does not kill",
        "Protogen: heart lacks WolfmedOrgan",
        "Protogen: lungs lack WolfmedOrgan",
        "Skrell: 29% blood does not arrest",
        "Skrell: brain lacks WolfmedBrain and WolfmedOrgan",
        "Skrell: brain removal does not kill",
        "Skrell: heart lacks WolfmedOrgan",
        "Skrell: lungs lack WolfmedOrgan",
        "SlimePerson: 29% blood does not arrest",
        "SlimePerson: brain lacks WolfmedBrain and WolfmedOrgan",
        "SlimePerson: brain removal does not kill",
        "SlimePerson: lungs lack WolfmedOrgan",
        "SlimePerson: no heart",
        // D (OD16): not wound hosts at all; stock thresholds, none of Wolfmed applies.
        "ProtoKin: not a wound host",
        "Shadekin: not a wound host",
        "Synth: not a wound host",
    ];

    [Test]
    public async Task KnownGapsAreExactlyTheReportTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        var map = await Pair.CreateTestMap();
        var gaps = new List<string>();
        var machines = new List<(string Species, EntityUid Body)>();

        await Server.WaitAssertion(() =>
        {
            var graph = SEntMan.System<SharedBodySystem>();
            var s = new WolfmedScenario(SEntMan);
            var shutdown = SEntMan.System<WolfmedShutdownSystem>();
            var mobState = SEntMan.System<MobStateSystem>();
            var protoManager = Server.ResolveDependency<IPrototypeManager>();

            foreach (var species in protoManager.EnumeratePrototypes<SpeciesPrototype>()
                         .Where(species => species.RoundStart)
                         .OrderBy(species => species.ID))
            {
                var body = SEntMan.SpawnEntity(species.Prototype, map.GridCoords);
                void Gap(string check) => gaps.Add($"{species.ID}: {check}");

                if (!SEntMan.HasComponent<WoundHostComponent>(body))
                {
                    Gap("not a wound host");
                    SEntMan.DeleteEntity(body);
                    continue;
                }

                var organs = graph.GetBodyOrgans(body).Select(organ => organ.Id).ToList();
                var mechanical = shutdown.IsMechanical(body);
                var brains = organs.Where(organ => SEntMan.HasComponent<BrainComponent>(organ)).ToList();
                var hearts = organs.Where(organ => SEntMan.HasComponent<HeartComponent>(organ)).ToList();

                if (mechanical)
                {
                    CheckOrgans(Gap, "core", brains);
                    CheckOrgans(Gap, "pump", hearts);

                    // The power check needs the charge loop, which only runs on a chassis with a mind.
                    var minds = SEntMan.System<SharedMindSystem>();
                    minds.TransferTo(minds.CreateMind(null).Owner, body);
                    machines.Add((species.ID, body));
                    continue;
                }

                if (brains.Count == 0)
                    Gap("no brain");
                else if (!brains.All(brain => SEntMan.HasComponent<WolfmedBrainComponent>(brain) &&
                                              SEntMan.HasComponent<WolfmedOrganComponent>(brain)))
                    Gap("brain lacks WolfmedBrain and WolfmedOrgan");

                CheckOrgans(Gap, "heart", hearts);
                CheckOrgans(Gap, "lungs", organs.Where(organ => SEntMan.HasComponent<LungComponent>(organ)).ToList());

                // 29% blood stops the heart.
                if (!SEntMan.HasComponent<BloodstreamComponent>(body))
                {
                    Gap("no bloodstream");
                }
                else
                {
                    s.SetBlood(body, 0.29f);
                    s.Advance(body, 1);
                    if (!s.Life.InArrest(body))
                        Gap("29% blood does not arrest");
                }

                // Taking the brain out kills.
                var second = SEntMan.SpawnEntity(species.Prototype, map.GridCoords);
                var brainOut = graph.GetBodyOrgans(second)
                    .FirstOrDefault(organ => SEntMan.HasComponent<BrainComponent>(organ.Id)).Id;
                if (brainOut == default || !graph.RemoveOrgan(brainOut) || !mobState.IsDead(second))
                    Gap("brain removal does not kill");

                SEntMan.DeleteEntity(body);
                SEntMan.DeleteEntity(second);
            }

            // A pulled cell, for every machine at once.
            foreach (var (species, body) in machines)
            {
                if (!SEntMan.System<ItemSlotsSystem>().TryGetSlot(body, "cell_slot", out var slot) ||
                    slot.Item is not { } cell)
                {
                    gaps.Add($"{species}: no power cell slot");
                    continue;
                }

                SEntMan.System<SharedContainerSystem>().Remove(cell, slot.ContainerSlot!);
            }
        });

        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var graph = SEntMan.System<SharedBodySystem>();
            var shutdown = SEntMan.System<WolfmedShutdownSystem>();
            var mobState = SEntMan.System<MobStateSystem>();

            foreach (var (species, body) in machines)
            {
                if (!shutdown.IsShutDown(body))
                    gaps.Add($"{species}: pulled power source does not shut down");

                // Taking the core out kills.
                var core = graph.GetBodyOrgans(body).FirstOrDefault(organ => SEntMan.HasComponent<BrainComponent>(organ.Id)).Id;
                if (core == default || !graph.RemoveOrgan(core) || !mobState.IsDead(body))
                    gaps.Add($"{species}: core removal does not kill");
            }
        });

        gaps.Sort(string.CompareOrdinal);
        TestContext.Out.WriteLine($"Species conformance, report mode: {gaps.Count} gap(s).");
        foreach (var gap in gaps)
            TestContext.Out.WriteLine($"        \"{gap}\",");

        var known = KnownGaps.OrderBy(gap => gap, System.StringComparer.Ordinal).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(gaps.Except(known), Is.Empty,
                "new species gaps: fix them, or excuse them in KnownGaps with the plan's reason.");
            Assert.That(known.Except(gaps), Is.Empty,
                "these species conform now: take them off KnownGaps.");
        });
    }

    /// <summary>Every organ of the kind carries Wolfmed organ health, and there is at least one.</summary>
    private void CheckOrgans(System.Action<string> gap, string name, List<EntityUid> organs)
    {
        if (organs.Count == 0)
            gap($"no {name}");
        else if (!organs.All(organ => SEntMan.HasComponent<WolfmedOrganComponent>(organ)))
            gap($"{name} lack{(name.EndsWith('s') ? string.Empty : "s")} WolfmedOrgan");
    }
}
