using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.AppearanceStudio.UI;

/// <summary>Small text helpers for the studio's windows.</summary>
public static class StudioText
{
    /// <summary>Plain text that wraps to a width, which a Label can't.</summary>
    public static RichTextLabel Wrapped(string text, string styleClass, float maxWidth = 0f)
    {
        var label = new RichTextLabel { StyleClasses = { styleClass }, HorizontalExpand = true };
        if (maxWidth > 0f)
            label.MaxWidth = maxWidth;

        label.SetMessage(FormattedMessage.FromUnformatted(text));
        return label;
    }
}
