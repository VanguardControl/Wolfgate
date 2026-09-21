using Content.Client._WF.Wolfmed.Overlays;
using Content.Shared.Mobs;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>The dying effects ramp in from half way to crit, carry on through crit, and stop at death.</summary>
[TestFixture]
[TestOf(typeof(WolfmedDyingEffectsSystem))]
public sealed class WolfmedDyingLevelTest
{
    [Test]
    public void LevelFollowsTheWayToDeath()
    {
        const float crit = 100f;
        const float dead = 200f;

        Assert.Multiple(() =>
        {
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Alive, 0f, crit, dead), Is.Zero);
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Alive, 50f, crit, dead), Is.Zero);
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Alive, 75f, crit, dead),
                Is.EqualTo(WolfmedDyingEffectsSystem.CritLevel / 2f).Within(0.001f));
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Critical, 100f, crit, dead),
                Is.EqualTo(WolfmedDyingEffectsSystem.CritLevel).Within(0.001f));
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Critical, 200f, crit, dead), Is.EqualTo(1f).Within(0.001f));
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Dead, 300f, crit, dead), Is.Zero);
        });
    }
}
