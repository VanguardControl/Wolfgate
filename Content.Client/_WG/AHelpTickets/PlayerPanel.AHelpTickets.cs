using Content.Client._WG.AHelpTickets;
using Content.Shared.Administration;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Administration.UI.PlayerPanel;

// Provide loadout on the Player Panel, the same grant the ticket panel offers.
public sealed partial class PlayerPanel
{
    /// <summary>Adds Provide loadout beside Job whitelists, for staff with the Admin flag.</summary>
    private void WGAddLoadoutButton()
    {
        if (!_adminManager.HasFlag(AdminFlags.Admin) || JobWhitelistsButton.Parent is not { } row)
            return;

        var button = new Button
        {
            Text = Loc.GetString("wg-ahelp-action-loadout"),
            ToolTip = Loc.GetString("wg-ahelp-action-loadout-tooltip"),
            SetWidth = 136,
            SetHeight = 27,
        };
        button.OnPressed += _ =>
        {
            if (TargetPlayer is { } player)
                WGLoadoutGrantWindow.Open(IoCManager.Resolve<IEntityManager>(), player, TargetUsername ?? player.ToString());
        };
        row.AddChild(button);
    }
}
