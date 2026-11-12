using System.Numerics;
using Content.Client._WF.AppearanceStudio.UI;
using Content.Shared._WF.AppearanceStudio;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.IntegrationTests.Tests._WF.AppearanceStudio;

/// <summary>The warp editor's brushes bend the warp: push drags pixels along, smooth evens them, restore undoes.</summary>
[TestFixture]
[TestOf(typeof(StudioWarpCanvas))]
public sealed class StudioWarpCanvasTest
{
    [Test]
    public async Task BrushesBendTheWarpTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });

        await pair.Client.WaitAssertion(() =>
        {
            var canvas = new StudioWarpCanvas { Radius = 3f, Strength = 1f };
            canvas.SetFrames(Frames());
            canvas.SetFacing(0);

            // Push: drag right across the middle, a quarter pixel at a time as the mouse reports it.
            canvas.Tool = StudioWarpTool.Push;
            canvas.BeginStroke(new Vector2(14f, 16f), false);
            for (var x = 14.25f; x <= 18f; x += 0.25f)
            {
                canvas.MoveStroke(new Vector2(x, 16f));
            }

            canvas.EndStroke();

            var middle = StudioWarp.Index(0, 16, 16);
            Assert.That(canvas.Warp.Dx[middle], Is.LessThan(0), "pixels under a rightward push draw from the left");
            Assert.That(canvas.Warp.IsNeutral(), Is.False);

            // Smooth evens a lone step out towards its neighbours.
            var pushed = canvas.Warp.Dx[middle];
            canvas.Tool = StudioWarpTool.Smooth;
            canvas.BeginStroke(new Vector2(16.5f, 16.5f), false);
            canvas.EndStroke();
            Assert.That(canvas.Warp.Dx[middle], Is.GreaterThanOrEqualTo(pushed), "smoothing pulls the step back towards its neighbours");

            // Restore puts pixels back.
            canvas.Tool = StudioWarpTool.Restore;
            for (var i = 0; i < 6; i++)
            {
                canvas.BeginStroke(new Vector2(16.5f, 16.5f), false);
                canvas.EndStroke();
            }

            Assert.That(canvas.Warp.Dx[middle], Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }

    private static Rgba32[][] Frames()
    {
        var frames = new Rgba32[4][];
        for (var facing = 0; facing < 4; facing++)
        {
            frames[facing] = new Rgba32[StudioWarp.Size * StudioWarp.Size];
            for (var i = 0; i < frames[facing].Length; i++)
            {
                frames[facing][i] = new Rgba32((byte) i, 0, 0, 255);
            }
        }

        return frames;
    }
}
