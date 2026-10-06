using System.IO;
using Content.Client._WF.CustomMarkings;
using Content.Shared._WF.CustomMarkings;
using NUnit.Framework;
using Robust.Shared.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Tests._WF.CustomMarkings;

/// <summary>The pixel sheet: its layout, the editor's operations and the PNG it travels as.</summary>
[TestFixture]
[TestOf(typeof(CustomMarkingArt))]
[TestOf(typeof(CustomMarkingPng))]
public sealed class CustomMarkingArtTest
{
    private const int Frame = CustomMarkingRules.FrameSize;
    private const int Sheet = CustomMarkingRules.SheetSize;

    private static readonly Rgba32 Red = new(255, 0, 0, 255);
    private static readonly Rgba32 Blue = new(0, 0, 255, 128);

    /// <summary>Facings sit on the sheet the way an RSI state lays out its four directions.</summary>
    [TestCase(CustomMarkingArt.South, 0, 0)]
    [TestCase(CustomMarkingArt.North, Frame, 0)]
    [TestCase(CustomMarkingArt.East, 0, Frame)]
    [TestCase(CustomMarkingArt.West, Frame, Frame)]
    public void SheetLayoutTest(int facing, int sheetX, int sheetY)
    {
        var art = new CustomMarkingArt();
        Assert.That(art.SetPixel(facing, 3, 5, Red), Is.True);
        Assert.That(art.SetPixel(facing, 3, 5, Red), Is.False, "setting the same value changes nothing");

        var at = ((sheetY + 5) * Sheet + sheetX + 3) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(art.Pixels[at], Is.EqualTo(255));
            Assert.That(art.Pixels[at + 3], Is.EqualTo(255));
            Assert.That(art.GetPixel(facing, 3, 5), Is.EqualTo(Red));
            Assert.That(art.IsBlank(), Is.False);
        });

        art.Clear(facing);
        Assert.That(art.IsBlank(), Is.True);
    }

    [Test]
    public void FromPixelsTest()
    {
        Assert.That(CustomMarkingArt.FromPixels(null), Is.Null);
        Assert.That(CustomMarkingArt.FromPixels(new byte[CustomMarkingRules.PixelBytes - 4]), Is.Null);

        // A transparent pixel can't carry colour: the same drawing always has the same bytes, and so one hash.
        var raw = new byte[CustomMarkingRules.PixelBytes];
        raw[0] = 200;
        raw[4] = 10;
        raw[7] = 255;
        var art = CustomMarkingArt.FromPixels(raw)!;
        Assert.Multiple(() =>
        {
            Assert.That(art.Pixels[0], Is.Zero);
            Assert.That(art.GetPixel(CustomMarkingArt.South, 1, 0), Is.EqualTo(new Rgba32(10, 0, 0, 255)));
            Assert.That(art.Pixels, Is.Not.SameAs(raw));
        });

        var edited = new CustomMarkingArt();
        edited.SetPixel(CustomMarkingArt.South, 0, 0, new Rgba32(200, 0, 0, 0));
        Assert.That(edited.IsBlank(), Is.True);
        Assert.That(edited.Pixels[0], Is.Zero);
    }

    [Test]
    public void ColourConversionTest()
    {
        for (var value = 0; value <= byte.MaxValue; value++)
        {
            var pixel = new Rgba32((byte) value, (byte) (255 - value), (byte) value, (byte) value);
            Assert.That(CustomMarkingArt.ToPixel(CustomMarkingArt.ToColor(pixel)), Is.EqualTo(pixel), $"channel value {value}");
        }

        Assert.That(CustomMarkingArt.ToPixel(new Color(2f, -1f, 0.5f, 1f)), Is.EqualTo(new Rgba32(255, 0, 128, 255)));
    }

    [Test]
    public void FillTest()
    {
        var art = new CustomMarkingArt();
        // A closed box from (2,2) to (6,6).
        for (var i = 2; i <= 6; i++)
        {
            art.SetPixel(CustomMarkingArt.East, i, 2, Red);
            art.SetPixel(CustomMarkingArt.East, i, 6, Red);
            art.SetPixel(CustomMarkingArt.East, 2, i, Red);
            art.SetPixel(CustomMarkingArt.East, 6, i, Red);
        }

        art.Fill(CustomMarkingArt.East, 4, 4, Blue);
        Assert.Multiple(() =>
        {
            Assert.That(art.GetPixel(CustomMarkingArt.East, 3, 3), Is.EqualTo(Blue));
            Assert.That(art.GetPixel(CustomMarkingArt.East, 5, 5), Is.EqualTo(Blue));
            Assert.That(art.GetPixel(CustomMarkingArt.East, 2, 2), Is.EqualTo(Red), "the outline stays");
            Assert.That(art.GetPixel(CustomMarkingArt.East, 7, 7).A, Is.Zero, "the fill stays inside");
            Assert.That(art.GetPixel(CustomMarkingArt.West, 4, 4).A, Is.Zero, "and on its facing");
        });

        // Filling the outside reaches every edge of the facing and nothing past it.
        art.Fill(CustomMarkingArt.East, 0, 0, Red);
        Assert.Multiple(() =>
        {
            Assert.That(art.GetPixel(CustomMarkingArt.East, Frame - 1, Frame - 1), Is.EqualTo(Red));
            Assert.That(art.GetPixel(CustomMarkingArt.East, 4, 4), Is.EqualTo(Blue));
            Assert.That(art.GetPixel(CustomMarkingArt.South, 0, Frame - 1).A, Is.Zero);
            Assert.That(art.GetPixel(CustomMarkingArt.West, 0, 0).A, Is.Zero);
        });

        Assert.DoesNotThrow(() => art.Fill(CustomMarkingArt.East, -1, 40, Red));
    }

    [Test]
    public void MoveAndMirrorTest()
    {
        var art = new CustomMarkingArt();
        art.SetPixel(CustomMarkingArt.East, 1, 2, Red);
        art.SetPixel(CustomMarkingArt.East, Frame - 1, 0, Blue);

        art.CopyFacing(CustomMarkingArt.East, CustomMarkingArt.West, true);
        Assert.Multiple(() =>
        {
            Assert.That(art.GetPixel(CustomMarkingArt.West, Frame - 2, 2), Is.EqualTo(Red));
            Assert.That(art.GetPixel(CustomMarkingArt.West, 0, 0), Is.EqualTo(Blue));
            Assert.That(art.GetPixel(CustomMarkingArt.East, 1, 2), Is.EqualTo(Red), "the source is left alone");
        });

        // Flipping a facing in place.
        art.CopyFacing(CustomMarkingArt.East, CustomMarkingArt.East, true);
        Assert.That(art.GetPixel(CustomMarkingArt.East, Frame - 2, 2), Is.EqualTo(Red));
        Assert.That(art.GetPixel(CustomMarkingArt.East, 1, 2).A, Is.Zero);

        art.Shift(CustomMarkingArt.East, 1, -1);
        Assert.Multiple(() =>
        {
            Assert.That(art.GetPixel(CustomMarkingArt.East, Frame - 1, 1), Is.EqualTo(Red));
            Assert.That(art.GetPixel(CustomMarkingArt.East, 1, 0).A, Is.Zero, "pixels pushed off the top are gone");
            Assert.That(art.GetPixel(CustomMarkingArt.West, Frame - 2, 2), Is.EqualTo(Red), "other facings don't move");
        });

        var before = art.Clone();
        art.Clear(CustomMarkingArt.West);
        Assert.That(art.SamePixels(before), Is.False);
        art.Restore(before.Pixels);
        Assert.That(art.SamePixels(before), Is.True);
    }

    [Test]
    public void FlipTest()
    {
        var art = new CustomMarkingArt();
        art.SetPixel(CustomMarkingArt.South, 10, 3, Red);
        art.SetPixel(CustomMarkingArt.South, 15, 4, Blue);
        art.SetPixel(CustomMarkingArt.South, 31, 5, Red);
        art.SetPixel(CustomMarkingArt.North, 10, 3, Red);

        // About column 15, a body's middle: its edges swap, and the middle stays.
        art.Flip(CustomMarkingArt.South, 30);
        Assert.Multiple(() =>
        {
            Assert.That(art.GetPixel(CustomMarkingArt.South, 20, 3), Is.EqualTo(Red));
            Assert.That(art.GetPixel(CustomMarkingArt.South, 10, 3).A, Is.Zero);
            Assert.That(art.GetPixel(CustomMarkingArt.South, 15, 4), Is.EqualTo(Blue));
            Assert.That(art.GetPixel(CustomMarkingArt.South, 31, 5).A, Is.Zero, "a pixel flipped off the facing is lost");
            Assert.That(art.GetPixel(CustomMarkingArt.North, 10, 3), Is.EqualTo(Red), "other facings stay");
        });

        // Twice is back where it started, less what fell off.
        art.Flip(CustomMarkingArt.South, 30);
        Assert.That(art.GetPixel(CustomMarkingArt.South, 10, 3), Is.EqualTo(Red));
        Assert.That(art.GetPixel(CustomMarkingArt.South, 31, 5).A, Is.Zero);

        // About the middle of the frame, the first and last columns swap.
        art.Flip(CustomMarkingArt.South, CustomMarkingRules.FrameSize - 1);
        Assert.That(art.GetPixel(CustomMarkingArt.South, 21, 3), Is.EqualTo(Red));
        Assert.That(art.GetPixel(CustomMarkingArt.South, 16, 4), Is.EqualTo(Blue));
    }

    [Test]
    public void WithoutTest()
    {
        var art = new CustomMarkingArt();
        art.SetPixel(CustomMarkingArt.South, 1, 1, Red);
        art.SetPixel(CustomMarkingArt.South, 2, 2, Blue);
        art.SetPixel(CustomMarkingArt.West, 1, 1, Red);

        var asked = 0;
        var cut = art.Without((facing, x, y) =>
        {
            asked++;
            return facing == CustomMarkingArt.South && x == 1;
        });

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(3), "only drawn pixels are asked about");
            Assert.That(cut.GetPixel(CustomMarkingArt.South, 1, 1).A, Is.Zero);
            Assert.That(cut.GetPixel(CustomMarkingArt.South, 2, 2), Is.EqualTo(Blue));
            Assert.That(cut.GetPixel(CustomMarkingArt.West, 1, 1), Is.EqualTo(Red));
            Assert.That(art.GetPixel(CustomMarkingArt.South, 1, 1), Is.EqualTo(Red), "the original is left alone");
            Assert.That(art.Without((_, _, _) => false).SamePixels(art), Is.True);
            Assert.That(art.Without((_, _, _) => true).IsBlank(), Is.True);
        });
    }

    [Test]
    public void FingerprintTest()
    {
        var art = new CustomMarkingArt();
        art.SetPixel(CustomMarkingArt.North, 5, 6, Red);
        var same = art.Clone();
        var other = art.Clone();
        other.SetPixel(CustomMarkingArt.North, 5, 7, Red);

        Assert.Multiple(() =>
        {
            Assert.That(art.Fingerprint(), Has.Length.EqualTo(16));
            Assert.That(art.Fingerprint(), Does.Match("^[0-9a-f]{16}$"));
            Assert.That(same.Fingerprint(), Is.EqualTo(art.Fingerprint()));
            Assert.That(other.Fingerprint(), Is.Not.EqualTo(art.Fingerprint()));
            Assert.That(new CustomMarkingArt().Fingerprint(), Is.Not.EqualTo(art.Fingerprint()));
        });
    }

    [Test]
    public void PngRoundTripTest()
    {
        var art = new CustomMarkingArt();
        art.SetPixel(CustomMarkingArt.South, 0, 0, Red);
        art.SetPixel(CustomMarkingArt.North, 31, 31, Blue);
        art.SetPixel(CustomMarkingArt.East, 7, 9, new Rgba32(1, 2, 3, 4));
        art.SetPixel(CustomMarkingArt.West, 16, 16, new Rgba32(250, 251, 252, 253));

        var read = CustomMarkingPng.Read(art.ToPng());
        Assert.That(read, Is.Not.Null);
        Assert.That(read!.SamePixels(art), Is.True);
    }

    [Test]
    public void ImportTest()
    {
        // One facing alone becomes the south facing.
        using var single = new Image<Rgba32>(Frame, Frame);
        single[4, 5] = Red;
        var art = CustomMarkingPng.Read(Png(single));
        Assert.That(art, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(art!.GetPixel(CustomMarkingArt.South, 4, 5), Is.EqualTo(Red));
            art.Clear(CustomMarkingArt.South);
            Assert.That(art.IsBlank(), Is.True);
        });

        using var wide = new Image<Rgba32>(Sheet, Frame);
        using var large = new Image<Rgba32>(Sheet * 2, Sheet * 2);
        Assert.Multiple(() =>
        {
            Assert.That(CustomMarkingPng.Read(Png(wide)), Is.Null, "not square");
            Assert.That(CustomMarkingPng.Read(Png(large)), Is.Null, "too big");
            Assert.That(CustomMarkingPng.Read(new byte[] { 1, 2, 3 }), Is.Null, "not an image");
            Assert.That(CustomMarkingPng.Read(new byte[CustomMarkingPng.MaxFileBytes + 1]), Is.Null, "too many bytes");
        });
    }

    private static byte[] Png(Image image)
    {
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
