using Content.Client._WF.Caverns;
using NUnit.Framework;

namespace Content.Tests._WF.Caverns;

/// <summary>When the z-level renderer draws the cavern under a ground layer, and at what depth.</summary>
[TestFixture]
[TestOf(typeof(WFCavernViewSystem))]
public sealed class CavernPassTest
{
    /// <summary>With the cavern known and a mouth in view, it is drawn one level below the ground, wherever the ground is drawn.</summary>
    [TestCase(0f, -1f)]
    [TestCase(-0.25f, -1.25f)]
    [TestCase(-1f, -2f)]
    [TestCase(-4f, -5f)]
    public void CavernDrawsOneLevelUnderTheGround(float groundDepth, float expected)
    {
        Assert.That(WFCavernViewSystem.CavernPassDepth(groundDepth, true, true), Is.EqualTo(expected));
    }

    /// <summary>Without the cavern's map, or with no mouth in view, the ground stays the floor of the view.</summary>
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public void GroundStaysTheFloorOtherwise(bool known, bool mouthInView)
    {
        Assert.That(WFCavernViewSystem.CavernPassDepth(0f, known, mouthInView), Is.Null);
    }
}
