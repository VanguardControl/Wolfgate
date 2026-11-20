using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client._WG.Wilderness;

/// <summary>
/// A one-line pill under the top menu: the zone's icon, title and place, and the mark's state. It opens to say what
/// the zone means for a few seconds after anything changes, and while the cursor is over it. Click it for the rule.
/// </summary>
public sealed class WGZoneBanner : PanelContainer
{
    private readonly StyleBoxFlat _box = new();
    private readonly TextureRect _icon;
    private readonly Label _title;
    private readonly Label _mark;
    private readonly BoxContainer _details;
    private readonly RichTextLabel _summary;
    private readonly RichTextLabel _markDetail;
    private readonly WGHazardStripe _stripe;

    private bool _hovered;

    /// <summary>Whether the details show whatever the cursor does; set by the HUD while it holds them open.</summary>
    public bool HoldOpen;

    /// <summary>Raised when the banner is clicked.</summary>
    public event Action? Clicked;

    public WGZoneBanner()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        var bold = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 12);
        var small = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);

        MouseFilter = MouseFilterMode.Stop;
        MaxWidth = 640;
        PanelOverride = _box;
        _box.BackgroundColor = Color.Black.WithAlpha(0.8f);
        _box.BorderThickness = new Thickness(1);
        _box.SetContentMarginOverride(StyleBox.Margin.All, 0);

        _icon = new TextureRect
        {
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            MinSize = new Vector2(20, 20),
            SetSize = new Vector2(20, 20),
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _title = new Label { FontOverride = bold, VerticalAlignment = VAlignment.Center, HorizontalExpand = true };
        _mark = new Label
        {
            FontOverride = bold,
            FontColorOverride = Color.FromHex("#ff5a50"),
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Visible = false,
        };

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(6, 3, 8, 3) };
        row.AddChild(_icon);
        row.AddChild(_title);
        row.AddChild(_mark);

        _summary = new RichTextLabel { HorizontalExpand = true };
        _markDetail = new RichTextLabel { HorizontalExpand = true, Visible = false };
        var hint = new Label { FontOverride = small, FontColorOverride = Color.FromHex("#9aa4ad"), Text = Loc.GetString("wg-zone-banner-hint") };
        _details = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8, 0, 8, 5), Visible = false };
        _details.AddChild(_summary);
        _details.AddChild(_markDetail);
        _details.AddChild(hint);

        _stripe = new WGHazardStripe { MinHeight = 2 };

        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        column.AddChild(row);
        column.AddChild(_details);
        column.AddChild(_stripe);
        AddChild(column);

        OnMouseEntered += _ => _hovered = true;
        OnMouseExited += _ => _hovered = false;
    }

    /// <summary>
    /// Fills the banner. <paramref name="mark"/> is the short mark state on the pill, <paramref name="markDetail"/>
    /// its explanation in the details; either may be null.
    /// </summary>
    public void Set(Color color, Texture? icon, string title, string summary, string? mark, string? markDetail, bool stripes)
    {
        _box.BorderColor = color.WithAlpha(0.7f);
        _icon.Texture = icon;
        _icon.Visible = icon != null;
        _title.Text = title;
        _title.FontColorOverride = color;
        _mark.Text = mark ?? string.Empty;
        _mark.Visible = mark != null;

        var message = new FormattedMessage();
        message.AddText(summary);
        _summary.SetMessage(message);

        _markDetail.Visible = markDetail != null;
        if (markDetail != null)
        {
            var detail = new FormattedMessage();
            detail.PushColor(Color.FromHex("#ff5a50"));
            detail.AddText(markDetail);
            detail.Pop();
            _markDetail.SetMessage(detail);
        }

        _stripe.Visible = stripes;
        _stripe.Color = color;
        _details.Visible = HoldOpen || _hovered;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        Clicked?.Invoke();
        args.Handle();
    }
}

/// <summary>A strip of diagonal hazard stripes in a colour and black.</summary>
public sealed class WGHazardStripe : Control
{
    public Color Color = Color.Red;

    private readonly Vector2[] _quad = new Vector2[4];

    public WGHazardStripe()
    {
        RectClipContent = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = PixelSize;
        if (size.X <= 0 || size.Y <= 0)
            return;

        handle.DrawRect(new UIBox2(0, 0, size.X, size.Y), Color.Black);
        var height = (float) size.Y;
        var step = MathF.Max(height * 2f, 6f);
        for (var x = -height; x < size.X; x += step * 2f)
        {
            _quad[0] = new Vector2(x, height);
            _quad[1] = new Vector2(x + height, 0f);
            _quad[2] = new Vector2(x + height + step, 0f);
            _quad[3] = new Vector2(x + step, height);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _quad, Color);
        }
    }
}
