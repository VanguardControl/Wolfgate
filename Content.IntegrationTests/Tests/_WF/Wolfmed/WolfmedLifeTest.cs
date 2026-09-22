#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>With thresholds gone, a wound host still dies of a lost head, no blood, or no air.</summary>
[TestFixture]
[TestOf(typeof(WolfmedLifeSystem))]
public sealed class WolfmedLifeTest : GameTest
{
    [Test]
    public async Task HeadLossBloodLossAndSuffocationKillTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mobState = entities.System<MobStateSystem>();
            var life = entities.System<WolfmedLifeSystem>();
            var graph = entities.System<SharedBodySystem>();

            var beheaded = entities.SpawnEntity("MobHuman", map.GridCoords);
            var head = graph.GetBodyChildrenOfType(beheaded, BodyPartType.Head).First().Id;
            Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(head), Is.True);
            Assert.That(life.Check((beheaded, entities.GetComponent<WolfmedConsciousnessComponent>(beheaded))),
                Is.EqualTo("no brain"));
            Assert.That(mobState.IsDead(beheaded), Is.True, "a body with no head kept living.");

            var bled = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(life.Check((bled, entities.GetComponent<WolfmedConsciousnessComponent>(bled))), Is.Null);
            entities.System<Content.Server.Body.Systems.BloodstreamSystem>().TryModifyBloodLevel(bled, FixedPoint2.New(-260));
            Assert.That(life.Check((bled, entities.GetComponent<WolfmedConsciousnessComponent>(bled))),
                Is.EqualTo("no blood"));
            Assert.That(mobState.IsDead(bled), Is.True);

            var choked = entities.SpawnEntity("MobHuman", map.GridCoords);
            var spec = new DamageSpecifier();
            spec.DamageDict[new ProtoId<DamageTypePrototype>("Asphyxiation")] = FixedPoint2.New(320);
            entities.System<DamageableSystem>().TryChangeDamage(choked, spec, ignoreResistances: true);
            Assert.That(life.Check((choked, entities.GetComponent<WolfmedConsciousnessComponent>(choked))),
                Is.EqualTo("no air"));
            Assert.That(mobState.IsDead(choked), Is.True);
        });
    }
}
