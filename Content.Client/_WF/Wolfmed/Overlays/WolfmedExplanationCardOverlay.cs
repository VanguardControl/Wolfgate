using System.Numerics;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>
/// M2 (plan §5.2): the explanation card, a small text panel low on the unconscious screen. The words come from
/// <see cref="WolfmedExplanationCard"/>; this only lays them out, with the coarse bar under them while Dying.
/// </summary>
public sealed class WolfmedExplanationCardOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceCache _cache = default!;

    private const int TitleSize = 18;
    private const int BodySize = 14;
    private static readonly Color Panel = Color.Black.WithAlpha(0.72f);
    private static readonly Color TitleColor = Color.FromHex("#e8d9a6");
    private static readonly Color BodyColor = Color.FromHex("#c9c3b8");
    private static readonly Color BarColor = Color.FromHex("#b3121a");

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Font _title;
    private readonly Font _body;

    /// <summary>Opacity of the whole card; the system fades it in and out.</summary>
    public float Alpha;

    public WolfmedExplanationCardOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 210; // over the faint's white-out and the dying view
        _title = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), TitleSize);
        _body = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), BodySize);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args) =>
        Alpha > 0.001f && _entities.HasComponent<WolfmedConsciousnessComponent>(_player.LocalEntity);

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } local ||
            !_entities.TryGetComponent(local, out WolfmedConsciousnessComponent? consciousness))
            return;

        _entities.TryGetComponent(local, out WolfmedCardComponent? card);
        var lines = WolfmedExplanationCard.Lines(_prototypes, consciousness, card);
        var bar = WolfmedExplanationCard.Bar(_prototypes, consciousness, card);
        if (lines.Count == 0)
            return;

        var handle = args.ScreenHandle;
        var bounds = WolfmedSyntheticHudLayout.Screen(args.ViewportBounds,
            (args.ViewportControl as Control)?.GlobalPixelPosition ?? Vector2i.Zero);
        var scale = Math.Clamp(bounds.Height / 900f, 0.75f, 1.6f);
        var width = MathF.Min(bounds.Width - 32f, 560f * scale);
        var padding = 12f * scale;
        var inner = width - padding * 2f;

        // Lay the text out first so the panel fits it.
        var rows = new List<(string Text, Font Font, Color Colour)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var font = i == 0 ? _title : _body;
            foreach (var row in Wrap(handle, font, lines[i], inner, scale))
                rows.Add((row, font, i == 0 ? TitleColor : BodyColor));
        }

        var lineHeight = BodySize * 1.45f * scale;
        var titleHeight = TitleSize * 1.4f * scale;
        var height = padding * 2f;
        foreach (var row in rows)
            height += row.Font == _title ? titleHeight : lineHeight;

        var barHeight = 10f * scale;
        if (bar != null)
            height += lineHeight + barHeight + 4f * scale;

        var left = bounds.Left + (bounds.Width - width) / 2f;
        var top = bounds.Bottom - height - bounds.Height * 0.12f;
        handle.DrawRect(new UIBox2(left, top, left + width, top + height), Panel.WithAlpha(Panel.A * Alpha));

        var y = top + padding;
        foreach (var (text, font, colour) in rows)
        {
            handle.DrawString(font, new Vector2(left + padding, y), text, scale, colour.WithAlpha(Alpha));
            y += font == _title ? titleHeight : lineHeight;
        }

        if (bar is not { } shown)
            return;

        handle.DrawString(_body, new Vector2(left + padding, y), shown.Label, scale, BodyColor.WithAlpha(Alpha));
        y += lineHeight;

        // Ten cells, filled for the tenths the brain still has. A bar, never a clock.
        var gap = 3f * scale;
        var cell = (inner - gap * 9f) / 10f;
        for (var i = 0; i < 10; i++)
        {
            var x = left + padding + i * (cell + gap);
            var box = new UIBox2(x, y, x + cell, y + barHeight);
            handle.DrawRect(box, (i < shown.Tenths ? BarColor : Color.DimGray.WithAlpha(0.4f)).WithAlpha(Alpha));
        }
    }

    /// <summary>Greedy word wrap to the card's inner width.</summary>
    private static IEnumerable<string> Wrap(DrawingHandleScreen handle, Font font, string text, float width, float scale)
    {
        var line = string.Empty;
        foreach (var word in text.Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && handle.GetDimensions(font, candidate, scale).X > width)
            {
                yield return line;
                line = word;
                continue;
            }

            line = candidate;
        }

        if (line.Length > 0)
            yield return line;
    }
}

/// <summary>
/// M2 (plan §5.2): a faint's own short white-out, so being briefly knocked out never looks like dying. The system sets
/// the strength; it pulses gently while it lasts.
/// </summary>
public sealed class WolfmedFaintOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public float Alpha;

    public WolfmedFaintOverlay()
    {
        ZIndex = 200;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args) => Alpha > 0.001f;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var bounds = WolfmedSyntheticHudLayout.Screen(args.ViewportBounds,
            (args.ViewportControl as Control)?.GlobalPixelPosition ?? Vector2i.Zero);
        args.ScreenHandle.DrawRect(bounds, Color.FromHex("#f4f1ea").WithAlpha(Alpha));
    }
}
