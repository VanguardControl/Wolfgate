using Content.Shared._WF.AppearanceStudio;
using Content.Shared._WF.CustomMarkings;
using NUnit.Framework;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Tests._WF.AppearanceStudio;

[TestFixture]
[TestOf(typeof(StudioWarp))]
public sealed class StudioWarpTest
{
    [Test]
    public void ArtRoundTripTest()
    {
        var warp = new StudioWarp();
        warp.Set(CustomMarkingArt.East, 10, 12, 3, -4);
        warp.Cut[StudioWarp.Index(CustomMarkingArt.North, 0, 0)] = true;

        var art = warp.ToArt();
        var pixel = art.GetPixel(0, CustomMarkingArt.East, 10, 12);
        Assert.Multiple(() =>
        {
            Assert.That(pixel, Is.EqualTo(new Rgba32(131, 132, 0, 255)), "red is 128 + x step, green 128 - y step");
            Assert.That(art.GetPixel(0, CustomMarkingArt.North, 0, 0).A, Is.Zero, "cut pixels draw nothing");
            Assert.That(StudioWarp.IsValid(art), Is.True);
        });

        var read = StudioWarp.FromArt(art)!;
        var i = StudioWarp.Index(CustomMarkingArt.East, 10, 12);
        Assert.Multiple(() =>
        {
            Assert.That(read.Dx[i], Is.EqualTo(3));
            Assert.That(read.Dy[i], Is.EqualTo(-4));
            Assert.That(read.Cut[StudioWarp.Index(CustomMarkingArt.North, 0, 0)], Is.True);
            Assert.That(read.IsNeutral(), Is.False);
        });
    }

    [Test]
    public void StepsStayInTheFrameTest()
    {
        var warp = new StudioWarp();
        warp.Set(CustomMarkingArt.South, 2, 30, -10, 10);

        var i = StudioWarp.Index(CustomMarkingArt.South, 2, 30);
        Assert.That(warp.Dx[i], Is.EqualTo(-2));
        Assert.That(warp.Dy[i], Is.EqualTo(1));
    }

    [Test]
    public void StepOutOfFrameIsInvalidTest()
    {
        var art = new StudioWarp().ToArt();
        // Draws from x = -1: whatever sits beside the item in the atlas.
        art.SetPixel(0, CustomMarkingArt.South, 0, 5, StudioWarp.Encode(-1, 0, false));
        Assert.That(StudioWarp.IsValid(art), Is.False);

        var half = new StudioWarp().ToArt();
        half.SetPixel(0, CustomMarkingArt.South, 4, 4, new Rgba32(128, 128, 0, 100));
        Assert.That(StudioWarp.IsValid(half), Is.False, "pixels are drawn or cut, never half");

        var blue = new StudioWarp().ToArt();
        blue.SetPixel(0, CustomMarkingArt.South, 4, 4, new Rgba32(128, 128, 7, 255));
        Assert.That(StudioWarp.IsValid(blue), Is.False, "blue is always 0, so one warp has one hash");
    }

    [Test]
    public void MirrorFlipsStepsTest()
    {
        var warp = new StudioWarp();
        warp.Set(CustomMarkingArt.East, 5, 7, 2, 1);

        warp.CopyFacing(CustomMarkingArt.East, CustomMarkingArt.West, true);

        var i = StudioWarp.Index(CustomMarkingArt.West, StudioWarp.Size - 1 - 5, 7);
        Assert.That(warp.Dx[i], Is.EqualTo(-2));
        Assert.That(warp.Dy[i], Is.EqualTo(1));
    }
}
