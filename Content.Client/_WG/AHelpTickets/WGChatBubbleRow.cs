using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client._WG.AHelpTickets;

/// <summary>Which side of the chat a bubble sits on.</summary>
public enum WGChatBubbleSide : byte
{
    Left,
    Right,
    Center,
}

/// <summary>
/// Holds one chat bubble: it takes the width its text needs, up to most of the row, and sits on the left, the right
/// or in the middle.
/// </summary>
public sealed class WGChatBubbleRow : Control
{
    /// <summary>The share of the row a bubble may take.</summary>
    private const float MaxShare = 0.78f;

    private readonly WGChatBubbleSide _side;

    public WGChatBubbleRow(WGChatBubbleSide side, Control bubble)
    {
        _side = side;
        HorizontalExpand = true;
        AddChild(bubble);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var limit = new Vector2(float.IsFinite(availableSize.X) ? availableSize.X * MaxShare : availableSize.X, availableSize.Y);
        var size = Vector2.Zero;
        foreach (var child in Children)
        {
            child.Measure(limit);
            size = Vector2.Max(size, child.DesiredSize);
        }

        return size;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        foreach (var child in Children)
        {
            var width = Math.Min(child.DesiredSize.X, finalSize.X * MaxShare);
            var x = _side switch
            {
                WGChatBubbleSide.Right => finalSize.X - width,
                WGChatBubbleSide.Center => (finalSize.X - width) / 2,
                _ => 0,
            };
            child.Arrange(UIBox2.FromDimensions(x, 0, width, child.DesiredSize.Y));
        }

        return finalSize;
    }
}
