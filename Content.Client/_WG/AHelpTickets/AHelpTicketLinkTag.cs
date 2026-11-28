using System.Diagnostics.CodeAnalysis;
using Content.Client._WG.Stylesheets;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.Shared._WG.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client._WG.AHelpTickets;

/// <summary>
/// <c>[ahelpticket=KEY/]</c>: a clickable link in a staff chat line that opens the ticket panel on that ticket. The
/// server puts it on requests for help with a ticket.
/// </summary>
public sealed partial class AHelpTicketLinkTag : IMarkupTagHandler
{
    public const string TagName = "ahelpticket";

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    public string Name => TagName;

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (!node.Value.TryGetLong(out var number) || number is not { } key)
            return false;

        var skin = WolfgateSkins.Get(_cfg.GetCVar(WolfgateCVars.UiStyle));
        var label = new Label
        {
            Text = Loc.GetString("wg-ahelp-assist-link"),
            FontColorOverride = skin.AccentDim,
            MouseFilter = Control.MouseFilterMode.Stop,
            DefaultCursorShape = Control.CursorShape.Hand,
        };
        label.OnMouseEntered += _ => label.FontColorOverride = skin.Accent;
        label.OnMouseExited += _ => label.FontColorOverride = skin.AccentDim;
        label.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            args.Handle();
            var controller = _ui.GetUIController<AHelpUIController>();
            controller.EnsureUIHelper();
            if (controller.UIHelper is WGTicketAHelpUIHandler tickets)
                tickets.OpenTicket((int) key);
        };

        control = label;
        return true;
    }
}
