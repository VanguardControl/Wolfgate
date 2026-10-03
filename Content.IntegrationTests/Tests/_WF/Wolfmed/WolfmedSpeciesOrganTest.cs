#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid.Prototypes;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// "Some species not coming with a full set of organs." The analyzer, surgery and organ damage only see an organ that
/// carries Wolfmed data, and most species' livers, stomachs and kidneys (the shared animal organs, Skrell, Protogen,
/// Synth, Arachnid) carried none, so a vulpkanin scanned with no liver or stomach. Every organ in a vital slot of every
/// species that is a wound host now carries it, and the goblin's lungs sit in their slot.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedOrganComponent))]
public sealed class WolfmedSpeciesOrganTest : GameTest
{
    private static readonly HashSet<string> VitalSlots = new()
    {
        "brain", "eyes", "heart", "lungs", "liver", "kidneys", "stomach", "posbrain", "pump", "core",
    };

    [Test]
    public async Task EverySpeciesOrganCarriesWolfmedDataTest()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.System<SharedBodySystem>();
            var bare = new List<string>();
            var unslotted = new List<string>();
            foreach (var species in SProtoMan.EnumeratePrototypes<SpeciesPrototype>())
            {
                var mob = SEntMan.SpawnEntity(species.Prototype, map.GridCoords);
                if (!SEntMan.HasComponent<WoundHostComponent>(mob))
                {
                    SEntMan.DeleteEntity(mob);
                    continue;
                }

                foreach (var (organ, component) in body.GetBodyOrgans(mob))
                {
                    var id = SEntMan.GetComponent<MetaDataComponent>(organ).EntityPrototype?.ID;
                    if (VitalSlots.Contains(component.SlotId) && !SEntMan.HasComponent<WolfmedOrganComponent>(organ))
                        bare.Add($"{species.ID}: {component.SlotId} ({id})");

                    var vital = SEntMan.HasComponent<LungComponent>(organ) || SEntMan.HasComponent<HeartComponent>(organ) ||
                                SEntMan.HasComponent<BrainComponent>(organ) || SEntMan.HasComponent<StomachComponent>(organ);
                    if (vital && string.IsNullOrEmpty(component.SlotId))
                        unslotted.Add($"{species.ID}: {id}");
                }

                SEntMan.DeleteEntity(mob);
            }

            Assert.Multiple(() =>
            {
                Assert.That(bare, Is.Empty, "organs Wolfmed cannot see.");
                Assert.That(unslotted, Is.Empty, "vital organs outside any slot.");
            });
        });
    }
}
