using Content.Shared._WG.MentorHelp;
using Content.Shared._WG.MentorObserve;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WG.MentorHelp;

/// <summary>Watching and critters in the Mentor Help window: the asker's prompt and controls, the mentor's buttons.</summary>
public sealed partial class MentorHelpWindow
{
    public event Action<int, MentorVisitKind, string?>? VisitRequested;
    public event Action<bool>? VisitAnswered;
    public event Action<bool>? VisitsEnded;
    public event Action? ReturnPressed;

    private readonly BoxContainer _visitBox = Column();
    private readonly RichTextLabel _visitText = new();
    private readonly BoxContainer _visitButtons = Row();
    private readonly OptionButton _critterPick = new();

    private Control BuildVisitBox()
    {
        _visitBox.AddChild(_visitText);
        _visitBox.AddChild(_visitButtons);
        _critterPick.OnItemSelected += args => _critterPick.SelectId(args.Id);
        return _visitBox;
    }

    /// <summary>The asker's side: answer a prompt, see that a mentor is watching or riding, end it.</summary>
    private void UpdateVisitBox(bool open)
    {
        var visits = _state.Visits;
        _visitButtons.RemoveAllChildren();
        _visitBox.Visible = open && (_state.VisitPrompt != null || visits.Watched || visits.CritterWithMe);
        if (!_visitBox.Visible)
            return;

        if (_state.VisitPrompt is { } prompt)
        {
            _visitText.SetMessage(FormattedMessage.FromUnformatted(prompt.Kind == MentorVisitKind.Critter
                ? Loc.GetString("wg-mentor-critter-prompt", ("animal", prompt.Animal))
                : Loc.GetString("wg-mentor-watch-prompt")));
            _visitButtons.AddChild(Button("wg-mentor-watch-allow", () => VisitAnswered?.Invoke(true)));
            _visitButtons.AddChild(Button("wg-mentor-watch-deny", () => VisitAnswered?.Invoke(false)));
            return;
        }

        _visitText.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("wg-mentor-watch-active")));
        if (visits.CritterRiding)
            _visitButtons.AddChild(Button("wg-mentor-critter-take-out", () => VisitsEnded?.Invoke(true)));

        _visitButtons.AddChild(Button("wg-mentor-watch-stop", () => VisitsEnded?.Invoke(false)));
    }

    /// <summary>The mentor's side on the selected ticket: watch, join as a critter, or go back.</summary>
    private void FillVisitActions(MentorHelpTicketView ticket)
    {
        _critterPick.Orphan();
        if (!ticket.ClaimedByMe && !_state.Staff)
            return;

        var visits = _state.Visits;
        if (visits.VisitingTicket == ticket.Id)
        {
            _ticketActions.AddChild(Button(visits.VisitingAsGhost ? "wg-mentor-unbind" : "wg-mentor-action-return", () => ReturnPressed?.Invoke()));
            return;
        }

        if (visits.VisitingTicket != null || visits.Pending.Contains(ticket.Id))
            return;

        _ticketActions.AddChild(Button("wg-mentor-watch", () => VisitRequested?.Invoke(ticket.Id, MentorVisitKind.Watch, null)));
        if (_state.Critters.Count == 0)
            return;

        var selected = _critterPick.SelectedId;
        _critterPick.Clear();
        for (var i = 0; i < _state.Critters.Count; i++)
        {
            _critterPick.AddItem(_state.Critters[i].Name, i);
        }

        _critterPick.SelectId(selected >= 0 && selected < _state.Critters.Count ? selected : 0);
        _critterPick.ToolTip = Loc.GetString("wg-mentor-critter-pick");
        _ticketActions.AddChild(_critterPick);
        _ticketActions.AddChild(Button("wg-mentor-critter", () =>
            VisitRequested?.Invoke(ticket.Id, MentorVisitKind.Critter, _state.Critters[_critterPick.SelectedId].Id)));
    }
}
