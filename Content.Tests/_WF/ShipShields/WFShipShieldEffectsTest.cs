using Content.Shared._WF.ShipShields;
using NUnit.Framework;

namespace Content.Tests._WF.ShipShields;

/// <summary>Checks shield condition and impact cues remain distinct.</summary>
[TestFixture]
public sealed class WFShipShieldEffectsTest
{
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
