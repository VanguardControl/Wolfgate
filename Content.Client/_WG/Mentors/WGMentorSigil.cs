using System.Numerics;
using Content.Shared._WG.Mentors;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WG.Mentors;

/// <summary>The tier's crown plate for the examine card's footer band, with a tooltip saying what it means.</summary>
public static class WGMentorSigil
{
    /// <summary>The sigil for an on-duty mentor's body, or null for anyone else.</summary>
    public static Control? For(EntityUid target, IEntityManager entities)
    {
        if (!entities.TryGetComponent<WGMentorMarkComponent>(target, out var mark))
            return null;

        var sprite = entities.System<SpriteSystem>();
        var texture = sprite.Frame0(new SpriteSpecifier.Rsi(WGMentorMarkSystem.MarkRsi, WGMentorMarkSystem.State(mark.Tier)));
        return new TextureRect
        {
            Texture = texture,
            Stretch = TextureRect.StretchMode.Scale,
            SetSize = new Vector2(16, 16),
            VerticalAlignment = Control.VAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = Loc.GetString("wg-mentor-card-tooltip"),
            MouseFilter = Control.MouseFilterMode.Stop,
        };
    }
}
