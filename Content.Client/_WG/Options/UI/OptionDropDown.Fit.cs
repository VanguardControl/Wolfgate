using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using UiButton = Robust.Client.UserInterface.Controls.Button;

namespace Content.Client.Options.UI;

/// <summary>
/// Holds an options drop-down at the width of its longest choice, so no choice overflows the button and the button
/// doesn't change width as choices are picked.
/// </summary>
public sealed partial class OptionDropDown
{
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (Button.Children.FirstOrDefault()?.Children.OfType<Label>().FirstOrDefault() is { } label)
        {
            var widest = WidestChoice(label);
            if (MathF.Abs(label.MinWidth - widest) > 0.5f)
                label.MinWidth = widest;
        }

        return base.MeasureOverride(availableSize);
    }

    /// <summary>The width of the longest choice in the button label's font, in virtual pixels.</summary>
    private float WidestChoice(Label label)
    {
        var font = label.FontOverride
                   ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
                       ? styled
                       : UserInterfaceManager.ThemeDefaults.LabelFont);

        var scale = UIScale;
        var widest = 0f;
        foreach (var choice in Choices(Button.OptionsScroll))
        {
            var width = 0f;
            foreach (var rune in choice.EnumerateRunes())
            {
                if (font.TryGetCharMetrics(rune, scale, out var metrics))
                    width += metrics.Advance;
            }

            widest = MathF.Max(widest, width);
        }

        return MathF.Ceiling(widest / scale);
    }

    /// <summary>The text of every choice in the button's popup.</summary>
    private static IEnumerable<string> Choices(Control control)
    {
        foreach (var child in control.Children)
        {
            if (child is UiButton { Text: { } text })
                yield return text;

            foreach (var text2 in Choices(child))
            {
                yield return text2;
            }
        }
    }
}
