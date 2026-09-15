using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// PartDamageVisualsComponent has projected per-part damage since WP5 with zero consumers (P3-D17). This proves
/// the server-side data itself is correct, and that it reaches the client's networked mirror, before WP11-4
/// builds a client-side reader on top of it.
/// </summary>
/// <remarks>PLAN3 §6.2 T-VISUALS. No sprite/screenshot assertion - this project prefers logic tests.</remarks>
[TestFixture]
[TestOf(typeof(WoundDamageProjectionSystem))]
public sealed class WolfmedVisualsTest : GameTest
{
    [Test]
    public async Task PartDamageProjectsToVisualsComponentTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var clientEntities = Pair.Client.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(body), Is.True,
                "MobHuman is not a wound host; the D21/D32 species wiring is missing.");

            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var leftArm = parts.Single(part => part.Component.PartType == BodyPartType.Arm &&
                                                part.Component.Symmetry == BodyPartSymmetry.Left).Id;
            var leftHand = parts.Single(part => part.Component.PartType == BodyPartType.Hand &&
                                                 part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            Assert.That(routing.TryApplyPartDamage(body, leftArm, Spec("Blunt", 10)), Is.True);
            // P3-D25's server-side anchor: LHand is a real key TryGetVisualLayer writes, even though the stock
            // six-layer targetLayers list (base.yml) cannot render it - that fold is WP11-4's job, not this one's.
            Assert.That(routing.TryApplyPartDamage(body, leftHand, Spec("Blunt", 6)), Is.True);

            var visual = entities.GetComponent<PartDamageVisualsComponent>(body);
            AssertProjection(visual);
        });

        await Pair.RunTicksSync(10); // let the client catch up on the networked component state.

        await Pair.Client.WaitAssertion(() =>
        {
            var clientBody = clientEntities.GetEntity(entities.GetNetEntity(body));
            var visual = clientEntities.GetComponent<PartDamageVisualsComponent>(clientBody);
            AssertProjection(visual);
        });
    }

    private static void AssertProjection(PartDamageVisualsComponent visual)
    {
        Assert.Multiple(() =>
        {
            Assert.That(visual.Damage.TryGetValue(HumanoidVisualLayers.LArm, out var arm), Is.True,
                "the left arm's own damage must land under its own visual layer.");
            Assert.That(arm!.DamageDict[new ProtoId<DamageTypePrototype>("Blunt")], Is.EqualTo(FixedPoint2.New(10)));

            Assert.That(visual.Damage.TryGetValue(HumanoidVisualLayers.LHand, out var hand), Is.True,
                "TryGetVisualLayer writes LHand for a Hand/Left part; this is the key the stock 6-layer list can't render.");
            Assert.That(hand!.DamageDict[new ProtoId<DamageTypePrototype>("Blunt")], Is.EqualTo(FixedPoint2.New(6)));

            AssertAbsentOrZero(visual, HumanoidVisualLayers.RArm);
            AssertAbsentOrZero(visual, HumanoidVisualLayers.RHand);
        });
    }

    private static void AssertAbsentOrZero(PartDamageVisualsComponent visual, HumanoidVisualLayers layer)
    {
        if (visual.Damage.TryGetValue(layer, out var damage))
            Assert.That(damage.GetTotal(), Is.EqualTo(FixedPoint2.Zero), $"{layer} must be absent or zero.");
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
