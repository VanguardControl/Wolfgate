using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._WF.Wolfmed.Life;

/// <summary>A yes/no window with the exact text the server sent: the Succumb and "left alive" dialogs.</summary>
public sealed class WolfmedChoiceWindow : DefaultWindow
{
    public readonly Button AcceptChoice;
    public readonly Button DenyChoice;
    private readonly RichTextLabel _text;

    public WolfmedChoiceWindow()
    {
        MinSize = new Vector2(440, 200);

        Contents.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 12,
            Children =
            {
                (_text = new RichTextLabel { VerticalExpand = true }),
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    Align = AlignMode.Center,
                    SeparationOverride = 20,
                    Children =
                    {
                        (AcceptChoice = new Button()),
                        (DenyChoice = new Button()),
                    },
                },
            },
        });
    }

    public void SetChoice(string title, string text, string accept, string deny)
    {
        Title = title;
        _text.SetMessage(FormattedMessage.FromUnformatted(text));
        AcceptChoice.Text = accept;
        DenyChoice.Text = deny;
    }
}
