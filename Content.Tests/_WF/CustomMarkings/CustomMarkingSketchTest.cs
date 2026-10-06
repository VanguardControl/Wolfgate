using Content.Client._WF.CustomMarkings;
using Content.Shared._WF.CustomMarkings;
using NUnit.Framework;
using Robust.Shared.Maths;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Tests._WF.CustomMarkings;

/// <summary>The editor's strokes and its undo and redo steps.</summary>
[TestFixture]
[TestOf(typeof(CustomMarkingSketch))]
public sealed class CustomMarkingSketchTest
{
    private const int South = CustomMarkingArt.South;

    private static readonly Rgba32 Red = new(255, 0, 0, 255);
    private static readonly Rgba32 Blue = new(0, 0, 255, 255);

    [Test]
    public void StrokeIsOneStepTest()
    {
        var opened = new CustomMarkingArt();
        var sketch = new CustomMarkingSketch(opened);
        Assert.That(sketch.CanUndo, Is.False);
        Assert.That(sketch.CanRedo, Is.False);

        // One drag: three moves, one step.
        sketch.Begin();
        sketch.Line(South, new Vector2i(0, 0), new Vector2i(0, 0), Red);
        sketch.Line(South, new Vector2i(0, 0), new Vector2i(5, 0), Red);
        sketch.Line(South, new Vector2i(5, 0), new Vector2i(5, 5), Red);
        sketch.End();

        Assert.Multiple(() =>
        {
            Assert.That(sketch.Art.GetPixel(South, 3, 0), Is.EqualTo(Red), "a fast drag leaves no gaps");
            Assert.That(sketch.Art.GetPixel(South, 5, 3), Is.EqualTo(Red));
            Assert.That(opened.IsBlank(), Is.True, "the art it was opened with is left alone");
            Assert.That(sketch.CanUndo, Is.True);
        });

        sketch.Undo();
        Assert.That(sketch.Art.IsBlank(), Is.True);
        Assert.That(sketch.CanUndo, Is.False);
        Assert.That(sketch.CanRedo, Is.True);

        sketch.Redo();
        Assert.That(sketch.Art.GetPixel(South, 5, 5), Is.EqualTo(Red));
        Assert.That(sketch.CanRedo, Is.False);
    }

    [Test]
    public void DiagonalAndOffCanvasTest()
    {
        var sketch = new CustomMarkingSketch(new CustomMarkingArt());
        sketch.Begin();
        sketch.Line(South, new Vector2i(-4, -4), new Vector2i(3, 3), Red);
        sketch.Line(South, new Vector2i(30, 10), new Vector2i(900, 10), Red);
        sketch.End();

        Assert.Multiple(() =>
        {
            Assert.That(sketch.Art.GetPixel(South, 0, 0), Is.EqualTo(Red));
            Assert.That(sketch.Art.GetPixel(South, 2, 2), Is.EqualTo(Red));
            Assert.That(sketch.Art.GetPixel(South, 1, 0).A, Is.Zero);
            Assert.That(sketch.Art.GetPixel(South, 31, 10), Is.EqualTo(Red), "the part inside the facing is drawn");
            Assert.That(sketch.Art.GetPixel(CustomMarkingArt.North, 0, 10).A, Is.Zero, "and nothing spills onto the next facing");
        });
    }

    [Test]
    public void StepsTest()
    {
        var sketch = new CustomMarkingSketch(new CustomMarkingArt());

        // A step that changes nothing isn't kept.
        sketch.Begin();
        sketch.End();
        sketch.Change(art => art.Clear(South));
        sketch.End();
        Assert.That(sketch.CanUndo, Is.False);

        sketch.Change(art => art.SetPixel(South, 1, 1, Red));
        sketch.Change(art => art.SetPixel(South, 2, 2, Blue));
        sketch.Undo();
        Assert.That(sketch.Art.GetPixel(South, 2, 2).A, Is.Zero);
        Assert.That(sketch.Art.GetPixel(South, 1, 1), Is.EqualTo(Red));

        // Drawing after an undo forgets what could have been redone.
        sketch.Change(art => art.SetPixel(South, 3, 3, Blue));
        Assert.That(sketch.CanRedo, Is.False);
        sketch.Redo();
        Assert.That(sketch.Art.GetPixel(South, 2, 2).A, Is.Zero);

        sketch.Undo();
        sketch.Undo();
        Assert.That(sketch.Art.IsBlank(), Is.True);
        sketch.Undo();
        Assert.That(sketch.Art.IsBlank(), Is.True, "undoing past the start does nothing");

        // An undo in the middle of a stroke closes it first, so it can be redone.
        sketch.Begin();
        sketch.Line(South, new Vector2i(9, 9), new Vector2i(9, 9), Red);
        sketch.Undo();
        Assert.That(sketch.Art.GetPixel(South, 9, 9).A, Is.Zero);
        sketch.Redo();
        Assert.That(sketch.Art.GetPixel(South, 9, 9), Is.EqualTo(Red));
    }

    [Test]
    public void StepLimitTest()
    {
        var sketch = new CustomMarkingSketch(new CustomMarkingArt());
        for (var i = 0; i < CustomMarkingSketch.MaxSteps + 10; i++)
        {
            var x = i % CustomMarkingRules.FrameSize;
            var y = i / CustomMarkingRules.FrameSize;
            sketch.Change(art => art.SetPixel(South, x, y, Red));
        }

        var undone = 0;
        while (sketch.CanUndo)
        {
            sketch.Undo();
            undone++;
        }

        Assert.That(undone, Is.EqualTo(CustomMarkingSketch.MaxSteps));
        Assert.That(sketch.Art.GetPixel(South, 9, 0), Is.EqualTo(Red), "the oldest steps can no longer be undone");
        Assert.That(sketch.Art.GetPixel(South, 10, 0).A, Is.Zero);
    }
}
