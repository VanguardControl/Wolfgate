#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 5, "someone somehow got 800 O2 damage": Asphyxiation and Bloodloss on a wound host's body stop at
/// wolfmed.airloss_cap, whatever keeps adding them. Bloodloss used to be exempt.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedBodyPartSystem))]
public sealed class WolfmedBodyDamageCeilingTest : GameTest
{
    [Test]
    public async Task BookkeepingDamageStopsAtTheCapTest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var damageable = SEntMan.System<DamageableSystem>();
            var cap = Server.CfgMan.GetCVar(WolfmedCVars.AirlossCap);
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var bloodloss = SProtoMan.Index<DamageTypePrototype>("Bloodloss");
            var asphyxiation = SProtoMan.Index<DamageTypePrototype>("Asphyxiation");

            for (var i = 0; i < 4; i++)
            {
                damageable.TryChangeDamage(body, new DamageSpecifier(bloodloss, FixedPoint2.New(cap)), ignoreResistances: true);
                damageable.TryChangeDamage(body, new DamageSpecifier(asphyxiation, FixedPoint2.New(cap)), ignoreResistances: true);
            }

            var damage = SEntMan.GetComponent<DamageableComponent>(body).Damage.DamageDict;
            Assert.Multiple(() =>
            {
                Assert.That(damage.GetValueOrDefault("Bloodloss").Float(), Is.EqualTo(cap).Within(0.01f), "bloodloss ran past the cap.");
                Assert.That(damage.GetValueOrDefault("Asphyxiation").Float(), Is.EqualTo(cap).Within(0.01f), "asphyxiation ran past the cap.");
            });
        });
    }
}
