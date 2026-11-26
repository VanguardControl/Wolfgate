using Content.Shared._WG.AHelpTickets;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.AHelpTickets;

/// <summary>Tells a player in their ahelp window which ticket they have and whether staff have picked it up.</summary>
public sealed class AHelpTicketStatusLabel : Label
{
    [Dependency] private IEntityManager _entities = default!;

    private readonly AHelpTicketClientSystem _tickets;

    public AHelpTicketStatusLabel()
    {
        IoCManager.InjectDependencies(this);
        _tickets = _entities.System<AHelpTicketClientSystem>();
        StyleClasses.Add("LabelSubText");

        _tickets.OwnStatusChanged += Show;
        if (_tickets.OwnStatus is { } status)
            Show(status);
        else
            Visible = false;
    }

    private void Show(AHelpTicketPlayerStatusMessage status)
    {
        Visible = true;
        Text = Loc.GetString("wg-ahelp-player-status",
            ("number", status.Number),
            ("state", status.State.ToString()),
            ("handler", status.Handler ?? "none"));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _tickets.OwnStatusChanged -= Show;
    }
}
