using System.Numerics;
using Content.Shared._WF.CustomMarkings;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Input;

namespace Content.Client._WF.CustomMarkings.UI;

/// <summary>
/// One facing of a custom marking, drawn large over a body. The body is the layers of a doll, drawn here one by
/// one so the art sits at the depth its placement gives it, exactly as it will in a round.
/// </summary>
public sealed partial class CustomMarkingCanvas : Control
{
    private const int Frame = CustomMarkingRules.FrameSize;

    private static readonly Color Backdrop = Color.FromHex("#2A2D33");
    private static readonly Color BackdropAlt = Color.FromHex("#33373F");
    private static readonly Color GridLine = Color.White.WithAlpha(0.07f);
    private static readonly Color HoverLine = Color.White.WithAlpha(0.9f);
    private static readonly Color MirrorHoverLine = Color.White.WithAlpha(0.45f);
    private static readonly Color MirrorGuide = Color.FromHex("#4FD3FF").WithAlpha(0.8f);
    private static readonly Color MirrorColumn = Color.FromHex("#4FD3FF").WithAlpha(0.14f);

    [Dependency] private IEntityManager _entMan = default!;

    private readonly CustomMarkingSystem _system;
    private bool _drawing;
    private bool _erasing;

    public CustomMarkingArt? Art;

    /// <summary>A finished facing, drawn as one texture in place of <see cref="Art"/>.</summary>
    public Texture? ArtTexture;

    /// <summary>Which facing is shown and drawn on: an index into the RSI's direction order.</summary>
    public int Facing;

    /// <summary>The doll whose layers are drawn as the body, if any.</summary>
    public EntityUid? Body;

    public CustomMarkingPlacement Placement;

    public bool ShowGrid;

    /// <summary>
    /// Where the mirror stands while drawing is mirrored, as <see cref="CustomMarkingSketch.MirrorAxis"/> gives it.
    /// Shows its line and the pixel across from the cursor.
    /// </summary>
    public int? MirrorAxis;

    /// <summary>The pixel under the cursor, outlined when the canvas takes input.</summary>
    public Vector2i? Hover { get; private set; }

    /// <summary>
    /// Raised for each pixel the cursor is pressed on or dragged to: the pixel, whether the erase button is the
    /// one held, and whether this press started the stroke.
    /// </summary>
    public event Action<Vector2i, bool, bool>? Stroke;

    public event Action? StrokeEnded;

    /// <param name="scale">Size of one art pixel. Zero for a canvas that only shows.</param>
    public CustomMarkingCanvas(int scale, bool interactive)
    {
        IoCManager.InjectDependencies(this);
        _system = _entMan.System<CustomMarkingSystem>();

        MinSize = new Vector2(Frame * scale, Frame * scale);
        HorizontalAlignment = HAlignment.Center;
        VerticalAlignment = VAlignment.Center;
        // One that only shows takes no clicks, so a button it sits in gets them.
        MouseFilter = interactive ? MouseFilterMode.Stop : MouseFilterMode.Ignore;
        RectClipContent = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var cell = PixelSize.X / (float) Frame;

        // A checkerboard of four-pixel squares shows through wherever nothing is drawn.
        handle.DrawRect(PixelSizeBox, Backdrop);
        for (var y = 0; y < Frame; y += 4)
        {
            for (var x = y % 8; x < Frame; x += 8)
            {
                handle.DrawRect(Cells(x, y, 4, cell), BackdropAlt);
            }
        }

        if (Body is { } body && _entMan.TryGetComponent(body, out SpriteComponent? sprite))
        {
            var depth = _system.GetLayerIndex((body, sprite), Placement) ?? int.MaxValue;
            DrawBody(handle, sprite, Facing, 0, depth, PixelSize);
            DrawArt(handle, cell);
            DrawBody(handle, sprite, Facing, depth, int.MaxValue, PixelSize);
        }
        else
        {
            DrawArt(handle, cell);
        }

        if (ShowGrid)
        {
            for (var i = 1; i < Frame; i++)
            {
                var at = MathF.Round(i * cell);
                handle.DrawLine(new Vector2(at, 0), new Vector2(at, PixelSize.Y), GridLine);
                handle.DrawLine(new Vector2(0, at), new Vector2(PixelSize.X, at), GridLine);
            }
        }

        if (MirrorAxis is { } axis)
        {
            // On a column when the axis is even: that column is tinted, with the line down its middle.
            if (axis % 2 == 0)
            {
                var column = Cells(axis / 2, 0, 1, cell);
                handle.DrawRect(new UIBox2(column.Left, 0, column.Right, PixelSize.Y), MirrorColumn);
            }

            var middle = MathF.Round((axis + 1) / 2f * cell);
            handle.DrawRect(new UIBox2(middle - 1, 0, middle + 1, PixelSize.Y), MirrorGuide);
        }

        if (Hover is not { } hover)
            return;

        handle.DrawRect(Cells(hover.X, hover.Y, 1, cell), HoverLine, false);
        if (MirrorAxis is { } mirror && CustomMarkingArt.InFrame(mirror - hover.X, hover.Y))
            handle.DrawRect(Cells(mirror - hover.X, hover.Y, 1, cell), MirrorHoverLine, false);
    }

    private void DrawArt(DrawingHandleScreen handle, float cell)
    {
        if (ArtTexture is { } texture)
        {
            handle.DrawTextureRect(texture, Cells(0, 0, Frame, cell));
            return;
        }

        if (Art == null)
            return;

        for (var y = 0; y < Frame; y++)
        {
            for (var x = 0; x < Frame; x++)
            {
                var pixel = Art.GetPixel(Facing, x, y);
                if (pixel.A != 0)
                    handle.DrawRect(Cells(x, y, 1, cell), CustomMarkingArt.ToColor(pixel));
            }
        }
    }

    /// <summary>
    /// Draws a doll's layers from one index up to another, each as its frame for a facing, with the frame scaled to
    /// <paramref name="size"/>. <see cref="CustomMarkingBodySampler"/> draws through this too, so what the colour
    /// picker reads is what the canvas shows.
    /// </summary>
    internal static void DrawBody(DrawingHandleScreen handle, SpriteComponent sprite, int facing, int from, int to, Vector2 size)
    {
        var cell = size.X / Frame;
        var index = -1;
        foreach (var spriteLayer in sprite.AllLayers)
        {
            index++;
            if (index < from || index >= to
                || spriteLayer is not SpriteComponent.Layer { Visible: true, Blank: false, CopyToShaderParameters: null } layer)
                continue;

            var texture = layer.Texture;
            if (layer.ActualRsi is { } rsi && rsi.TryGetState(layer.State, out var state))
                texture = state.GetFrame(state.RsiDirections == RsiDirectionType.Dir1 ? RsiDirection.South : (RsiDirection) facing, 0);

            if (texture == null)
                continue;

            var drawn = (Vector2) texture.Size * cell;
            var centre = size / 2f + new Vector2(layer.Offset.X, -layer.Offset.Y) * Frame * cell;
            handle.DrawTextureRect(texture, UIBox2.FromDimensions(centre - drawn / 2f, drawn), layer.Color * sprite.Color);
        }
    }

    /// <summary>The screen box of a square of art pixels, with edges rounded so neighbours never leave a seam.</summary>
    private static UIBox2 Cells(int x, int y, int span, float cell)
    {
        return new UIBox2(MathF.Round(x * cell), MathF.Round(y * cell), MathF.Round((x + span) * cell), MathF.Round((y + span) * cell));
    }

    private Vector2i PixelAt(Vector2 position)
    {
        var cell = PixelSize.X / (float) Frame;
        return new Vector2i((int) MathF.Floor(position.X / cell), (int) MathF.Floor(position.Y / cell));
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        var erase = args.Function == EngineKeyFunctions.UIRightClick;
        if (MouseFilter != MouseFilterMode.Stop || args.Function != EngineKeyFunctions.UIClick && !erase)
            return;

        _drawing = true;
        _erasing = erase;
        Stroke?.Invoke(PixelAt(args.RelativePixelPosition), erase, true);
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (!_drawing || args.Function != (_erasing ? EngineKeyFunctions.UIRightClick : EngineKeyFunctions.UIClick))
            return;

        _drawing = false;
        StrokeEnded?.Invoke();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        var pixel = PixelAt(args.RelativePixelPosition);
        Hover = MouseFilter == MouseFilterMode.Stop && CustomMarkingArt.InFrame(pixel.X, pixel.Y) ? pixel : null;
        if (_drawing)
            Stroke?.Invoke(pixel, _erasing, false);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        Hover = null;
    }
}
