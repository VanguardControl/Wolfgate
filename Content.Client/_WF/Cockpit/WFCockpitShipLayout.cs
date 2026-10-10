using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Cockpit;

/// <summary>Places hull details beside the plot only when the MFD has room for readable control columns.</summary>
public sealed class WFCockpitShipLayout : Control
{
    private const float WideWidth = 680;
    private const float Gap = 6;
    private readonly Control _plot;
    private readonly Control _details;
    private readonly GridContainer _telemetry;
    private readonly GridContainer _overlays;

    /// <summary>Owns the temporary layout around the existing leased plot, gauges and overlay controls.</summary>
    public WFCockpitShipLayout(Control plot, Control details, GridContainer telemetry, GridContainer overlays)
    {
        Name = "CockpitShipLayout";
        HorizontalExpand = VerticalExpand = true;
        _plot = plot;
        _details = details;
        _telemetry = telemetry;
        _overlays = overlays;
        AddChild(plot);
        AddChild(details);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        Reflow(availableSize.X);
        var (plot, details) = Regions(availableSize);
        _plot.Measure(plot.Size);
        _details.Measure(details.Size);
        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        Reflow(finalSize.X);
        var (plot, details) = Regions(finalSize);
        _plot.Measure(plot.Size);
        _details.Measure(details.Size);
        _plot.Arrange(plot);
        _details.Arrange(details);
        return finalSize;
    }

    private void Reflow(float width)
    {
        var wide = width >= WideWidth;
        var columns = wide ? 3 : 2;
        if (_telemetry.Columns != columns)
            _telemetry.Columns = columns;
        columns = wide ? 2 : 1;
        if (_overlays.Columns != columns)
            _overlays.Columns = columns;
    }

    private static (UIBox2 Plot, UIBox2 Details) Regions(Vector2 size)
    {
        if (size.X >= WideWidth)
        {
            var width = Math.Max(0, size.X - Gap);
            var plotWidth = width * 0.38f;
            return (UIBox2.FromDimensions(Vector2.Zero, new Vector2(plotWidth, size.Y)),
                UIBox2.FromDimensions(new Vector2(plotWidth + Gap, 0), new Vector2(width - plotWidth, size.Y)));
        }
        var height = Math.Max(0, size.Y - Gap);
        var plotHeight = height * 0.7f;
        return (UIBox2.FromDimensions(Vector2.Zero, new Vector2(size.X, plotHeight)),
            UIBox2.FromDimensions(new Vector2(0, plotHeight + Gap), new Vector2(size.X, height - plotHeight)));
    }
}
