using Content.Shared._WF.CustomMarkings;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.CustomMarkings;

/// <summary>A marking open in the editor: its art, and the steps that can be undone and redone.</summary>
public sealed class CustomMarkingSketch
{
    public const int MaxSteps = 64;

    /// <summary>Farther from the facing than any cursor gets while drawing on it.</summary>
    private const int MaxReach = 4096;

    public readonly CustomMarkingArt Art;

    private readonly List<byte[]> _undo = new();
    private readonly List<byte[]> _redo = new();
    private bool _open;

    public CustomMarkingSketch(CustomMarkingArt art)
    {
        Art = art.Clone();
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Opens a step: everything drawn until <see cref="End"/> is undone together.</summary>
    public void Begin()
    {
        if (_open)
            return;

        _open = true;
        _undo.Add(Art.Pixels.AsSpan().ToArray());
        if (_undo.Count > MaxSteps)
            _undo.RemoveAt(0);
    }

    /// <summary>Closes the open step, dropping it if nothing changed. Does nothing when no step is open.</summary>
    public void End()
    {
        if (!_open)
            return;

        _open = false;
        if (_undo[^1].AsSpan().SequenceEqual(Art.Pixels))
            _undo.RemoveAt(_undo.Count - 1);
        else
            _redo.Clear();
    }

    /// <summary>Runs one edit as a step of its own.</summary>
    public void Change(Action<CustomMarkingArt> edit)
    {
        Begin();
        edit(Art);
        End();
    }

    /// <summary>
    /// Draws a straight run of pixels, as a fast drag skips some between two mouse positions. Ends may lie outside
    /// the facing; only the part inside is drawn.
    /// </summary>
    public void Line(int facing, Vector2i from, Vector2i to, Rgba32 ink)
    {
        from = Bound(from);
        to = Bound(to);
        var steps = Math.Max(1, Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)));
        for (var i = 0; i <= steps; i++)
        {
            var x = (int) MathF.Round(from.X + (to.X - from.X) * (i / (float) steps));
            var y = (int) MathF.Round(from.Y + (to.Y - from.Y) * (i / (float) steps));
            if (CustomMarkingArt.InFrame(x, y))
                Art.SetPixel(facing, x, y, ink);
        }
    }

    /// <summary>Keeps a point within reach, so a line to it is a bounded amount of work.</summary>
    private static Vector2i Bound(Vector2i point)
    {
        return new Vector2i(Math.Clamp(point.X, -MaxReach, MaxReach), Math.Clamp(point.Y, -MaxReach, MaxReach));
    }

    public void Undo()
    {
        Step(_undo, _redo);
    }

    public void Redo()
    {
        Step(_redo, _undo);
    }

    private void Step(List<byte[]> from, List<byte[]> to)
    {
        End();
        if (from.Count == 0)
            return;

        to.Add(Art.Pixels.AsSpan().ToArray());
        Art.Restore(from[^1]);
        from.RemoveAt(from.Count - 1);
    }
}
