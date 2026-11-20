using System.Linq;
using System.Text;
using Content.Client._WG.MentorHelp;
using Content.Client._WG.Mentors;
using Content.Shared._WG.MentorHelp;
using Robust.Shared.Network;

namespace Content.Client.Administration.UI.Bwoink;

public sealed partial class BwoinkControl
{
    private WGClientMentorSystem? _wgMentors;
    private WGClientMentorHelpSystem? _wgMentorHelp;

    /// <summary>Wires the Mentor Help buttons and refreshes the player list when mentors or tickets change.</summary>
    private void WgInitMentorHelp()
    {
        _entMan.EntitySysManager.TryGetEntitySystem(out _wgMentors);
        _entMan.EntitySysManager.TryGetEntitySystem(out _wgMentorHelp);

        MentorHelpStaffView.OnPressed += _ => _ui.GetUIController<MentorHelpUIController>().Open();
        SendToMentorHelp.OnPressed += _ =>
        {
            if (_currentPlayer != null && _wgMentorHelp != null)
                _wgMentorHelp.SendFromAHelp(_currentPlayer.SessionId);
        };
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (_wgMentors != null)
            _wgMentors.RosterChanged += WgRefreshMarks;

        if (_wgMentorHelp != null)
            _wgMentorHelp.Changed += WgRefreshMarks;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (_wgMentors != null)
            _wgMentors.RosterChanged -= WgRefreshMarks;

        if (_wgMentorHelp != null)
            _wgMentorHelp.Changed -= WgRefreshMarks;
    }

    private void WgUpdateMentorHelp()
    {
        SendToMentorHelp.Disabled = _currentPlayer == null;
        MentorTicketMarker.Visible = _currentPlayer != null && WgHasOpenTicket(_currentPlayer.SessionId);
    }

    /// <summary>The mentor badge and the open-question mark before a player's name in the list.</summary>
    private void WgAppendMentorMarks(StringBuilder sb, NetUserId player)
    {
        if (_wgMentors?.Mentors.Contains(player) == true)
            sb.Append('(').Append(Loc.GetString("wg-mhelp-ahelp-badge")).Append(") ");

        if (WgHasOpenTicket(player))
            sb.Append("? ");
    }

    private bool WgHasOpenTicket(NetUserId player)
    {
        return _wgMentorHelp?.Tickets.Values.Any(t => t.AskerId == player && t.Status != MentorTicketStatus.Closed) == true;
    }

    private void WgRefreshMarks()
    {
        WgUpdateMentorHelp();
        ChannelSelector.PlayerListContainer.DirtyList();
    }
}
