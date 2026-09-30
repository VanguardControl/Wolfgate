using Content.Shared._WF.ShipShields;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.ShipShields;

/// <summary>Checks shield condition and impact cues remain distinct.</summary>
[TestFixture]
public sealed class WFShipShieldEffectsTest
{
    [TestCase("#FF3333")]
    [TestCase("#9933FF")]
    [TestCase("#3399FF")]
    [TestCase("#FF9933")]
    [TestCase("#50C878")]
    [TestCase("#FF0A70")]
    public void GeneratorColourSurvivesAtFullHealthAndBlendsWithDamage(string hex)
    {
        var generator = Color.FromHex(hex);
        Assert.That(WFShipShieldEffects.HealthColor(1f, generator), Is.EqualTo(generator));
        Assert.That(WFShipShieldEffects.HealthColor(2f, generator), Is.EqualTo(generator));
        var amber = WFShipShieldEffects.HealthColor(0.45f);
        var midpoint = WFShipShieldEffects.HealthColor(0.725f, generator);
        var expected = Color.InterpolateBetween(amber, generator, 0.5f);
        Assert.That(midpoint.R, Is.EqualTo(expected.R).Within(0.00001f));
        Assert.That(midpoint.G, Is.EqualTo(expected.G).Within(0.00001f));
        Assert.That(midpoint.B, Is.EqualTo(expected.B).Within(0.00001f));
        Assert.That(WFShipShieldEffects.HealthColor(0.45f, generator), Is.EqualTo(amber));
        Assert.That(WFShipShieldEffects.HealthColor(0f, generator), Is.EqualTo(WFShipShieldEffects.HealthColor(0f)));
        var idle = WFShipShieldEffects.Appearance(generator, 0f, 0f, 0f);
        Assert.That(idle.Tint, Is.EqualTo(Color.FromSrgb(generator)));
        var hit = WFShipShieldEffects.Appearance(generator, 0f, 0f, 1f);
        Assert.That(hit.Tint, Is.Not.EqualTo(idle.Tint));
    }

    [Test]
    public void ImpactsBrightenTheRimWithoutMaskingItsHealthColour()
    {
        var health = WFShipShieldEffects.HealthColor(0.28f, Color.FromHex("#3399FF"));
        var idle = WFShipShieldEffects.Appearance(health, 0f, 0f, 0f, 0.28f);
        var hit = WFShipShieldEffects.Appearance(health, 1f, 1f, 1f, 0.28f);
        Assert.That(hit.SurfaceTint, Is.EqualTo(Color.FromSrgb(health)));
        Assert.That(hit.SurfaceTint, Is.EqualTo(idle.SurfaceTint));
        Assert.That(hit.Surface, Is.GreaterThan(idle.Surface));
        Assert.That(hit.Tint, Is.Not.EqualTo(hit.SurfaceTint));
    }

    [Test]
    public void ImpactRevealsHexesMoreStronglyThanTheIdleSurface()
    {
        var health = WFShipShieldEffects.HealthColor(1f);
        var idle = WFShipShieldEffects.Appearance(health, 0f, 0f, 0f);
        var hit = WFShipShieldEffects.Appearance(health, WFShipShieldEffects.Heat(0f, 0f, 1f),
            WFShipShieldEffects.Wave(0f, 0f, 1f), WFShipShieldEffects.Flash(0f, 0f, 1f));
        Assert.That(idle.Surface, Is.LessThan(0.3f));
        Assert.That(idle.Hexes * WFShipShieldMesh.HexOpacity(0f), Is.InRange(0.1f, 0.15f));
        Assert.That(hit.Surface, Is.GreaterThan(idle.Surface * 5f));
        Assert.That(hit.Hexes, Is.GreaterThan(idle.Hexes * 10f));
    }

    [Test]
    public void DamagedShieldsBecomeMoreVisibleWithoutAnActiveImpact()
    {
        var healthy = WFShipShieldEffects.Appearance(WFShipShieldEffects.HealthColor(1f), 0f, 0f, 0f, 1f);
        var damaged = WFShipShieldEffects.Appearance(WFShipShieldEffects.HealthColor(0.5f), 0f, 0f, 0f, 0.5f);
        var critical = WFShipShieldEffects.Appearance(WFShipShieldEffects.HealthColor(0f), 0f, 0f, 0f, 0f);
        Assert.That(healthy.Surface, Is.LessThan(0.2f));
        Assert.That(damaged.Surface, Is.GreaterThan(healthy.Surface));
        Assert.That(critical.Surface, Is.GreaterThan(damaged.Surface));
        Assert.That(damaged.Hexes, Is.GreaterThan(healthy.Hexes));
        Assert.That(critical.Hexes, Is.GreaterThan(damaged.Hexes));
    }

    [Test]
    public void FlashStaysLocalAndHeatOutlastsTheRipple()
    {
        Assert.That(WFShipShieldEffects.Flash(8f, 0f, 1f), Is.LessThan(0.01f));
        Assert.That(WFShipShieldEffects.Flash(0f, -1f, 1f), Is.Zero);
        Assert.That(WFShipShieldEffects.Flash(0f, 0.65f, 1f), Is.Zero);
        Assert.That(WFShipShieldEffects.Wave(0f, 3f, 1f), Is.Zero);
        Assert.That(WFShipShieldEffects.Heat(0f, 3f, 1f), Is.GreaterThan(0f));
        Assert.That(WFShipShieldEffects.Heat(0f, 7.99f, 1f), Is.LessThan(0.001f));
        Assert.That(WFShipShieldEffects.Heat(0f, 8f, 1f), Is.Zero);
    }

    [Test]
    public void RepeatedHitsLeaveBrighterRedderHexesThenReturnToHealthColour()
    {
        var health = WFShipShieldEffects.HealthColor(1f);
        var heat = WFShipShieldEffects.Heat(0f, 1f, 1f);
        var single = WFShipShieldEffects.Appearance(health, heat, 0f, 0f);
        var repeated = WFShipShieldEffects.Appearance(health, heat * 3f, 0f, 0f);
        Assert.That(repeated.Tint.R, Is.GreaterThan(single.Tint.R));
        Assert.That(repeated.Tint.B, Is.LessThan(single.Tint.B));
        Assert.That(repeated.Hexes, Is.GreaterThan(single.Hexes));
        var cooled = WFShipShieldEffects.Appearance(health, WFShipShieldEffects.Heat(0f, 8f, 1f), 0f, 0f);
        Assert.That(cooled, Is.EqualTo(WFShipShieldEffects.Appearance(health, 0f, 0f, 0f)));
        var damaged = WFShipShieldEffects.Appearance(WFShipShieldEffects.HealthColor(0.2f), 0f, 0f, 0f);
        Assert.That(damaged.Tint.R, Is.GreaterThan(damaged.Tint.B));
    }

    [Test]
    public void ShieldHealthChangesFromBlueThroughAmberToRed()
    {
        var healthy = WFShipShieldEffects.HealthColor(1f);
        var damaged = WFShipShieldEffects.HealthColor(0.45f);
        var critical = WFShipShieldEffects.HealthColor(0f);
        Assert.Multiple(() =>
        {
            Assert.That(healthy.B, Is.GreaterThan(healthy.R));
            Assert.That(damaged.R, Is.GreaterThan(damaged.B));
            Assert.That(damaged.G, Is.GreaterThan(critical.G));
            Assert.That(critical.R, Is.GreaterThan(critical.G * 5f));
            Assert.That(WFShipShieldEffects.HealthColor(2f), Is.EqualTo(healthy));
            Assert.That(WFShipShieldEffects.HealthColor(-1f), Is.EqualTo(critical));
            Assert.That(WFShipShieldEffects.HealthColor(float.NaN), Is.EqualTo(critical));
        });
    }

    [Test]
    public void RepeatedImpactsAccumulateLocalHeatAndCool()
    {
        var initial = WFShipShieldEffects.Heat(0f, 0f, 1f);
        var previous = WFShipShieldEffects.Heat(0f, 1f, 1f);
        Assert.Multiple(() =>
        {
            Assert.That(initial + previous, Is.GreaterThan(initial));
            Assert.That(previous, Is.LessThan(initial));
            Assert.That(WFShipShieldEffects.Heat(8f, 0f, 1f), Is.LessThan(initial * 0.02f));
            Assert.That(WFShipShieldEffects.Heat(0f, 9f, 1f), Is.Zero);
            Assert.That(WFShipShieldEffects.Heat(0f, -1f, 1f), Is.Zero);
        });
    }

    [Test]
    public void ImpactWaveTravelsAwayFromItsOriginAndExpires()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFShipShieldEffects.Wave(8f, 0.5f, 1f), Is.GreaterThan(WFShipShieldEffects.Wave(0f, 0.5f, 1f)));
            Assert.That(WFShipShieldEffects.Wave(8f, 0f, 1f), Is.LessThan(WFShipShieldEffects.Wave(8f, 0.5f, 1f)));
            Assert.That(WFShipShieldEffects.Wave(48f, 3f, 1f), Is.Zero);
        });
    }
}
