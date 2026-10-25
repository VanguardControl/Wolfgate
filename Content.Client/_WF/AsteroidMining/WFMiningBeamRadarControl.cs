using System.Numerics;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._WF.AsteroidMining;

/// <summary>A top-down plot of nearby asteroids, sized and tinted by their leading ore, with the gun's reach.</summary>
public sealed class WFMiningBeamRadarControl : Control
{
    public Action<NetEntity>? OnTargetSelected;
    public WFMiningBeamTargetEntry[] Targets = Array.Empty<WFMiningBeamTargetEntry>();
    public WFMiningBeamEmitterEntry? Emitter;
    public NetEntity? Selected;
    public float Range = 150f;

    public WFMiningBeamRadarControl()
    {
        MinSize = new Vector2(360, 360);
        HorizontalExpand = true;
        VerticalExpand = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    private float PlotScale(Vector2 size)
    {
        return MathF.Min(size.X, size.Y) * 0.47f / MathF.Max(Range, 1f);
    }

    private Vector2 Plot(Vector2 position, Vector2 size)
    {
        return size / 2f + new Vector2(position.X, -position.Y) * PlotScale(size);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = (Vector2) PixelSize;
        var center = size / 2f;
        var radius = MathF.Min(size.X, size.Y) * 0.47f;
        var gridColor = new Color(0.25f, 0.2f, 0.1f);
        handle.DrawRect(new UIBox2(Vector2.Zero, size), new Color(0.04f, 0.03f, 0.02f));
        handle.DrawCircle(center, radius, gridColor, false);
        handle.DrawCircle(center, radius / 2f, gridColor, false);
        handle.DrawLine(center - new Vector2(radius, 0f), center + new Vector2(radius, 0f), gridColor);
        handle.DrawLine(center - new Vector2(0f, radius), center + new Vector2(0f, radius), gridColor);

        if (Emitter is { } emitter)
            DrawReach(handle, size, emitter);
        handle.DrawCircle(center, 4f * UIScale, Color.White);

        var scale = PlotScale(size);
        foreach (var target in Targets)
        {
            var position = Plot(target.Position, size);
            var plotted = MathF.Max(3f * UIScale, target.Radius * scale);
            var selected = Selected == target.Entity;
            var firing = Emitter is { Active: true } dish && dish.Target == target.Entity;
            handle.DrawCircle(position, plotted, target.Color.WithAlpha(0.35f + 0.5f * target.Charge));
            handle.DrawCircle(position, plotted, firing ? Color.Orange : selected ? Color.Yellow : target.Color, false);
            if (selected)
                handle.DrawCircle(position, plotted + 5f * UIScale, Color.Yellow, false);
            if (firing && Emitter is { } source)
                handle.DrawLine(Plot(source.Position, size), position, Color.Orange);
        }
    }

    /// <summary>The gun's barrel line out to its reach; it turns to aim, so this only shows where it points now.</summary>
    private void DrawReach(DrawingHandleScreen handle, Vector2 size, WFMiningBeamEmitterEntry emitter)
    {
        var origin = Plot(emitter.Position, size);
        var end = Plot(emitter.Position + emitter.Direction * emitter.MaxRange, size);
        handle.DrawCircle(origin, emitter.MaxRange * PlotScale(size), Color.Orange.WithAlpha(0.25f), false);
        handle.DrawLine(origin, end, Color.Orange.WithAlpha(0.7f));
        handle.DrawCircle(origin, 3f * UIScale, Color.Orange);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        NetEntity? closest = null;
        var best = float.MaxValue;
        var size = (Vector2) Size;
        var scale = PlotScale(size);
        foreach (var target in Targets)
        {
            var distance = Vector2.Distance(Plot(target.Position, size), args.RelativePosition);
            var reach = MathF.Max(12f, target.Radius * scale + 6f);
            if (distance > reach || distance >= best)
                continue;
            best = distance;
            closest = target.Entity;
        }

        if (closest is { } entity)
        {
            OnTargetSelected?.Invoke(entity);
            args.Handle();
        }
    }
}
