using System.Linq;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.CombatConsole;

/// <summary>Fits every connected weapon into the available battery panel without a scrolling parent.</summary>
public sealed class WFWeaponGrid : BoxContainer
{
    /// <summary>Smallest preferred row height before another weapon column is added.</summary>
    public float MinimumRowHeight { get; set; } = 34;

    public WFWeaponGrid()
    {
        HorizontalExpand = VerticalExpand = true;
        RectClipContent = true;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        Layout(availableSize, false);
        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        Layout(finalSize, true);
        return finalSize;
    }

    private void Layout(Vector2 available, bool arrange)
    {
        var buttons = Children.OfType<Button>().Where(button => button.Visible).ToArray();
        if (buttons.Length == 0 || !float.IsFinite(available.X) || !float.IsFinite(available.Y))
            return;
        const float gap = 4;
        var preferredColumns = Math.Max(1, (int) ((available.X + gap) / (128 + gap)));
        var maximumRows = Math.Max(1, (int) ((available.Y + gap) / (Math.Max(1, MinimumRowHeight) + gap)));
        var columns = Math.Clamp(Math.Max(preferredColumns, (buttons.Length + maximumRows - 1) / maximumRows), 1, buttons.Length);
        var rows = (buttons.Length + columns - 1) / columns;
        var cell = new Vector2(Math.Max(0, (available.X - gap * (columns - 1)) / columns),
            Math.Max(0, (available.Y - gap * (rows - 1)) / rows));
        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            var dense = cell.Y < 48 || cell.X < 108;
            var tight = dense && MinimumRowHeight < 34;
            var restyle = button.HasStyleClass("WfWeaponDense") != dense || button.HasStyleClass("WfWeaponTight") != tight;
            if (button.HasStyleClass("WfWeaponDense") != dense)
            {
                if (dense)
                    button.AddStyleClass("WfWeaponDense");
                else
                    button.RemoveStyleClass("WfWeaponDense");
            }
            if (button.HasStyleClass("WfWeaponTight") != tight)
            {
                if (tight)
                    button.AddStyleClass("WfWeaponTight");
                else
                    button.RemoveStyleClass("WfWeaponTight");
            }
            if (restyle)
                WFInstrumentTheme.Switch(button);
            var gauge = button.Children.OfType<WFGlassGauge>().FirstOrDefault();
            var instruments = cell.Y >= 104;
            if (gauge != null)
                gauge.Visible = instruments;
            var margin = new Thickness(2, 0, 2, instruments ? 58 : 0);
            if (button.Label.Margin != margin)
                button.Label.Margin = margin;
            button.Label.VerticalAlignment = instruments ? VAlignment.Top : VAlignment.Center;
            if (button.MinHeight != 0)
                button.MinHeight = 0;
            if (button.MinWidth != 0)
                button.MinWidth = 0;
            button.Measure(cell);
            if (arrange)
                button.Arrange(UIBox2.FromDimensions(new Vector2(i % columns, i / columns) * (cell + new Vector2(gap)), cell));
        }
    }
}
