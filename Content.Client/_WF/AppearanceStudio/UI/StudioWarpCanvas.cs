using System.Numerics;
using Content.Client._WF.CustomMarkings.UI;
using Content.Shared._WF.AppearanceStudio;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._WF.AppearanceStudio.UI;

/// <summary>The tools of the warp editor.</summary>
public enum StudioWarpTool : byte
{
    /// <summary>Drag the item's pixels along with the mouse.</summary>
    Push,

    /// <summary>Even out the steps under the brush.</summary>
    Smooth,

    /// <summary>Put pixels back where they were.</summary>
    Restore,

    /// <summary>Stop drawing pixels; right click brings them back.</summary>
    Cut,
}

/// <summary>
/// One facing of a warp, drawn magnified over the body it fits: the item as the warp bends it, the brush, and cut
/// pixels tinted. Strokes edit the warp in place.
/// </summary>
public sealed class StudioWarpCanvas : Control
{
    private const int Size = StudioWarp.Size;

    private static readonly Color Backdrop = Color.FromHex("#1b1d22");
    private static readonly Color CutTint = Color.Red.WithAlpha(0.35f);
    private static readonly Color BrushLine = Color.White.WithAlpha(0.6f);

    private readonly IClyde _clyde;

    /// <summary>Steps being edited, kept fractional so slow drags still move pixels.</summary>
    private readonly float[] _fx = new float[StudioWarp.Facings * Size * Size];

    private readonly float[] _fy = new float[StudioWarp.Facings * Size * Size];

    private Rgba32[][]? _frames;
    private OwnedTexture? _texture;
    private bool _textureStale = true;

    private StudioWarp _warp = new();
    private bool _stroking;
    private bool _alternate;
    private Vector2 _lastPixel;
    private Vector2? _hover;

    public int Facing { get; private set; }
    public StudioWarpTool Tool = StudioWarpTool.Push;
    public float Radius = 3f;
    public float Strength = 1f;

    /// <summary>Draws the item unwarped, to compare.</summary>
    public bool ShowOriginal;

    /// <summary>The doll drawn under the item, and the layers of it the item itself draws (left out).</summary>
    public SpriteComponent? Body;

    public HashSet<int>? ItemLayers;

    /// <summary>Where the item is moved to on this facing, in pixels, +Y up.</summary>
    public Vector2i ItemOffset;

    /// <summary>Raised as a stroke starts, so the window can keep an undo step.</summary>
    public event Action? StrokeStarted;

    /// <summary>Raised as a stroke ends, so the window can show the warp on the doll.</summary>
    public event Action? StrokeEnded;

    public StudioWarpCanvas()
    {
        _clyde = IoCManager.Resolve<IClyde>();
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(384, 384);
    }

    public StudioWarp Warp => _warp;

    /// <summary>The item's frames, south, north, east and west, each row by row.</summary>
    public void SetFrames(Rgba32[][]? frames)
    {
        _frames = frames;
        _textureStale = true;
    }

    public void SetWarp(StudioWarp warp)
    {
        _warp = warp.Clone();
        for (var i = 0; i < _fx.Length; i++)
        {
            _fx[i] = _warp.Dx[i];
            _fy[i] = _warp.Dy[i];
        }

        _textureStale = true;
    }

    public void SetFacing(int facing)
    {
        Facing = Math.Clamp(facing, 0, StudioWarp.Facings - 1);
        _textureStale = true;
    }

    /// <summary>Call after changing <see cref="Warp"/> from outside (copying a facing, clearing).</summary>
    public void WarpChanged()
    {
        SetWarp(_warp);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Backdrop);
        var cell = PixelSize.X / Size;

        if (Body != null)
            CustomMarkingCanvas.DrawBody(handle, Body, Facing, 0, int.MaxValue, PixelSize, leftOut: ItemLayers);

        if (_textureStale)
            RebuildTexture();

        if (_texture != null)
        {
            var shift = new Vector2(ItemOffset.X, -ItemOffset.Y) * cell;
            handle.DrawTextureRect(_texture, UIBox2.FromDimensions(shift, PixelSize));
        }

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (_warp.Cut[StudioWarp.Index(Facing, x, y)])
                    handle.DrawRect(UIBox2.FromDimensions(new Vector2(x, y) * cell, new Vector2(cell)), CutTint);
            }
        }

        if (_hover is { } hover)
            handle.DrawCircle(hover * cell, Radius * cell, BrushLine, false);
    }

    private void RebuildTexture()
    {
        _textureStale = false;
        _texture?.Dispose();
        _texture = null;
        if (_frames == null)
            return;

        var source = _frames[Facing];
        using var image = new Image<Rgba32>(Size, Size);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (ShowOriginal)
                {
                    image[x, y] = source[y * Size + x];
                    continue;
                }

                var i = StudioWarp.Index(Facing, x, y);
                if (_warp.Cut[i])
                    continue;

                image[x, y] = source[(y + _warp.Dy[i]) * Size + x + _warp.Dx[i]];
            }
        }

        _texture = _clyde.LoadTextureFromImage(image, "wf-studio-warp");
    }

    private Vector2 PixelAt(Vector2 position)
    {
        return position / (PixelSize.X / Size);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        var alternate = args.Function == EngineKeyFunctions.UIRightClick;
        if (args.Function != EngineKeyFunctions.UIClick && !alternate)
            return;

        StrokeStarted?.Invoke();
        _stroking = true;
        _alternate = alternate;
        _lastPixel = PixelAt(args.RelativePixelPosition);
        Paint(_lastPixel, Vector2.Zero);
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (!_stroking || args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.UIRightClick)
            return;

        _stroking = false;
        StrokeEnded?.Invoke();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        var pixel = PixelAt(args.RelativePixelPosition);
        _hover = pixel;
        if (!_stroking)
            return;

        Paint(pixel, pixel - _lastPixel);
        _lastPixel = pixel;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hover = null;
    }

    private void Paint(Vector2 centre, Vector2 moved)
    {
        var reach = (int) MathF.Ceiling(Radius);
        var cx = (int) MathF.Floor(centre.X);
        var cy = (int) MathF.Floor(centre.Y);
        for (var y = cy - reach; y <= cy + reach; y++)
        {
            for (var x = cx - reach; x <= cx + reach; x++)
            {
                if (x < 0 || y < 0 || x >= Size || y >= Size)
                    continue;

                var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                if (distance > Radius)
                    continue;

                var weight = (1f - distance / (Radius + 0.5f)) * Strength;
                PaintPixel(x, y, moved, weight);
            }
        }

        _textureStale = true;
    }

    private void PaintPixel(int x, int y, Vector2 moved, float weight)
    {
        var i = StudioWarp.Index(Facing, x, y);
        switch (Tool)
        {
            case StudioWarpTool.Push when !_alternate:
                // Content follows the drag: each pixel draws from further back along it.
                _fx[i] -= moved.X * weight;
                _fy[i] -= moved.Y * weight;
                break;
            case StudioWarpTool.Smooth:
                var (sx, sy, count) = (0f, 0f, 0);
                for (var ny = y - 1; ny <= y + 1; ny++)
                {
                    for (var nx = x - 1; nx <= x + 1; nx++)
                    {
                        if (nx < 0 || ny < 0 || nx >= Size || ny >= Size)
                            continue;

                        var n = StudioWarp.Index(Facing, nx, ny);
                        sx += _fx[n];
                        sy += _fy[n];
                        count++;
                    }
                }

                _fx[i] += (sx / count - _fx[i]) * Math.Min(1f, weight);
                _fy[i] += (sy / count - _fy[i]) * Math.Min(1f, weight);
                break;
            case StudioWarpTool.Restore:
            case StudioWarpTool.Push when _alternate:
                _fx[i] -= _fx[i] * Math.Min(1f, weight);
                _fy[i] -= _fy[i] * Math.Min(1f, weight);
                break;
            case StudioWarpTool.Cut:
                _warp.Cut[i] = !_alternate;
                return;
        }

        _warp.Set(Facing, x, y, (int) MathF.Round(_fx[i]), (int) MathF.Round(_fy[i]));
        // Keep the fractional step within what the frame allows, so it can't run away off the edge.
        _fx[i] = Math.Clamp(_fx[i], -x, Size - 1 - x);
        _fy[i] = Math.Clamp(_fy[i], -y, Size - 1 - y);
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _texture?.Dispose();
        _texture = null;
        _textureStale = true;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _texture?.Dispose();
        SetFrames(null);
    }
}
