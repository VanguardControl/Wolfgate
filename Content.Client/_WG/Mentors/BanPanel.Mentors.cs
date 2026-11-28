using System.Linq;
using Content.Shared._WG.Mentors;

namespace Content.Client.Administration.UI.BanPanel;

public sealed partial class BanPanel
{
    /// <summary>The Mentor group on the Roles tab: a mentor ban and a Mentor Help mute, sent as their role strings.</summary>
    private void WGAddMentorRoles()
    {
        CreateRoleGroup(Loc.GetString("wg-mentor-ban-panel-mentor"), MentorBans.Roles, Color.Gold);

        foreach (var box in _roleCheckboxes.Where(b => MentorBans.IsRole(b.Text ?? "")))
        {
            box.ToolTip = Loc.GetString(box.Text == MentorBans.HelpRole
                ? "wg-mentor-ban-panel-help"
                : "wg-mentor-ban-panel-mentor");
        }
    }
}
