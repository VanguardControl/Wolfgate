using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WG.AHelpTickets;

/// <summary>Asks a staffer whether to give a player their job loadout for free or charge the player's bank for it.</summary>
public sealed class WGLoadoutGrantWindow : DefaultWindow
{
    /// <summary>The staffer chose; true charges the player.</summary>
    public event Action<bool>? Chosen;

    /// <param name="player">Who gets the loadout.</param>
    /// <param name="price">What it costs, if the server could work it out.</param>
    public WGLoadoutGrantWindow(string player, int? price)
    {
        Title = Loc.GetString("wg-ahelp-loadout-title");
        MinSize = new Vector2(360, 120);

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        var text = new RichTextLabel();
        text.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("wg-ahelp-loadout-prompt", ("player", player), ("price", price ?? -1))));
        box.AddChild(text);

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        var free = new Button { Text = Loc.GetString("wg-ahelp-loadout-free"), HorizontalExpand = true };
        var charge = new Button
        {
            Text = Loc.GetString("wg-ahelp-loadout-charge", ("price", price ?? -1)),
            HorizontalExpand = true,
            Disabled = price is null or 0,
        };
        var cancel = new Button { Text = Loc.GetString("wg-ahelp-loadout-cancel"), HorizontalExpand = true };
        free.OnPressed += _ => Choose(false);
        charge.OnPressed += _ => Choose(true);
        cancel.OnPressed += _ => Close();
        buttons.AddChild(free);
        buttons.AddChild(charge);
        buttons.AddChild(cancel);
        box.AddChild(buttons);

        Contents.AddChild(box);
    }

    private void Choose(bool charge)
    {
        Chosen?.Invoke(charge);
        Close();
    }
}
