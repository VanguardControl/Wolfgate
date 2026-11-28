using System.Numerics;
using Content.Shared._WG.AHelpTickets;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Client._WG.AHelpTickets;

/// <summary>
/// Asks a staffer whether to give a player their job loadout for free or charge the player's bank for it. The price
/// is fetched fresh when it opens, and both choices wait for it.
/// </summary>
public sealed class WGLoadoutGrantWindow : DefaultWindow
{
    private readonly string _player;
    private readonly RichTextLabel _text = new();
    private readonly Button _free;
    private readonly Button _charge;

    /// <summary>The staffer chose; true charges the player.</summary>
    public event Action<bool>? Chosen;

    /// <param name="player">Who gets the loadout, as shown.</param>
    public WGLoadoutGrantWindow(string player)
    {
        _player = player;
        Title = Loc.GetString("wg-ahelp-loadout-title");
        MinSize = new Vector2(380, 130);

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        box.AddChild(_text);

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _free = new Button { Text = Loc.GetString("wg-ahelp-loadout-free"), HorizontalExpand = true, Disabled = true };
        _charge = new Button { Text = Loc.GetString("wg-ahelp-loadout-charge", ("price", -1)), HorizontalExpand = true, Disabled = true };
        var cancel = new Button { Text = Loc.GetString("wg-ahelp-loadout-cancel"), HorizontalExpand = true };
        _free.OnPressed += _ => Choose(false);
        _charge.OnPressed += _ => Choose(true);
        cancel.OnPressed += _ => Close();
        buttons.AddChild(_free);
        buttons.AddChild(_charge);
        buttons.AddChild(cancel);
        box.AddChild(buttons);
        Contents.AddChild(box);

        SetText(Loc.GetString("wg-ahelp-loadout-checking", ("player", _player)));
    }

    /// <summary>Opens the window for a player, asks the server for the current price and sends the choice.</summary>
    public static WGLoadoutGrantWindow Open(IEntityManager entities, NetUserId player, string name)
    {
        var tickets = entities.System<AHelpTicketClientSystem>();
        var window = new WGLoadoutGrantWindow(name);

        void OnCard(AHelpTicketPlayerCardMessage card)
        {
            if (card.Player == player)
                window.SetPrice(card.LoadoutPrice);
        }

        tickets.CardReceived += OnCard;
        window.OnClose += () => tickets.CardReceived -= OnCard;
        window.Chosen += charge => tickets.GiveLoadout(player, charge);
        tickets.RequestCard(player);
        window.OpenCentered();
        return window;
    }

    /// <summary>The price came back; null means the player has no body, job or loadout to give.</summary>
    public void SetPrice(int? price)
    {
        if (price is not { } cost)
        {
            SetText(Loc.GetString("wg-ahelp-loadout-none-client", ("player", _player)));
            _free.Disabled = _charge.Disabled = true;
            return;
        }

        SetText(Loc.GetString("wg-ahelp-loadout-prompt", ("player", _player), ("price", cost)));
        _free.Disabled = false;
        _charge.Disabled = cost == 0;
        _charge.Text = Loc.GetString("wg-ahelp-loadout-charge", ("price", cost));
    }

    private void SetText(string text)
    {
        _text.SetMessage(FormattedMessage.FromUnformatted(text));
    }

    private void Choose(bool charge)
    {
        Chosen?.Invoke(charge);
        Close();
    }
}
