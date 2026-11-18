using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WG.AdminTech;

/// <summary>
/// Asks the admin spawning another player whether that player gets Return, the action that deletes the spawned
/// body and frees them. Closing the window spawns nobody.
/// </summary>
public sealed class WGReturnPromptWindow : DefaultWindow
{
    public WGReturnPromptWindow(string player, Action<bool> onAnswer)
    {
        Title = Loc.GetString("wg-observer-return-prompt-title");
        MinSize = new Vector2(360, 140);

        var text = new RichTextLabel { MaxWidth = 340, Margin = new Thickness(0, 0, 0, 8) };
        text.SetMessage(Loc.GetString("wg-observer-return-prompt-text", ("player", FormattedMessage.EscapeText(player))));

        var yes = new Button { Text = Loc.GetString("wg-observer-return-prompt-yes"), HorizontalExpand = true };
        var no = new Button { Text = Loc.GetString("wg-observer-return-prompt-no"), HorizontalExpand = true };
        yes.OnPressed += _ => { onAnswer(true); Close(); };
        no.OnPressed += _ => { onAnswer(false); Close(); };

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        buttons.AddChild(yes);
        buttons.AddChild(no);

        var layout = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        layout.AddChild(text);
        layout.AddChild(buttons);
        Contents.AddChild(layout);
    }
}
