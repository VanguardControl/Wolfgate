using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Components;
using Content.Server.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

[TestFixture]
[TestOf(typeof(WoundBleedingSystem))]
public sealed class WolfmedBleedingLifecycleTest : GameTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task HealingBelowBleedingThresholdUpdatesBloodstreamTest(bool otherBleedingWound)
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var wounds = entities.System<WoundSystem>();
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var torso = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var bloodstream = entities.GetComponent<BloodstreamComponent>(body);
            if (otherBleedingWound)
                Assert.That(wounds.CreateOrMergeWound(torso, "PiercingWound", 10), Is.Not.Null);
            var remainingRate = bloodstream.BleedAmount;
            var wound = wounds.CreateOrMergeWound(torso, "SlashWound", 10)!.Value;
            Assert.That(bloodstream.BleedAmount, Is.GreaterThan(remainingRate));

            // Below severity 9, SlashWound loses its bleeding component while the wound remains.
            Assert.That(wounds.ChangeSeverity(wound, -2), Is.True);
            Assert.That(entities.HasComponent<WoundBleedingComponent>(wound), Is.False);
            Assert.That(bloodstream.BleedAmount, Is.EqualTo(remainingRate).Within(0.0001f),
                "Removing a bleeding effect must remove its contribution from the body immediately.");
            var diagnostics = analyzer.BuildWoundDiagnostics(body)!;
            Assert.That(diagnostics.Parts.Values.Sum(part => part.BleedingRate),
                Is.EqualTo(bloodstream.BleedAmount).Within(0.0001f));

            // Removing the now non-bleeding wound must not leave an invisible bleed behind.
            wounds.ClearWounds(torso);
            Assert.That(analyzer.BuildWoundDiagnostics(body)!.Parts.Values.Sum(part => part.BleedingRate),
                Is.Zero);
            Assert.That(bloodstream.BleedAmount, Is.Zero);
        });
    }
}
