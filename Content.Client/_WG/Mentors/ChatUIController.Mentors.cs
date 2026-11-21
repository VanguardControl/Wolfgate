using Content.Client._WG.Mentors;
using Content.Shared.Administration;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    /// <summary>Whether the local player may use mentor chat: an active mentor, or staff with Adminhelp or Event.</summary>
    private bool WGMentorChat =>
        _ent.EntitySysManager.TryGetEntitySystem<WGClientMentorSystem>(out var mentors) && mentors.IsMentor
        || _admin.HasFlag(AdminFlags.Adminhelp)
        || _admin.HasFlag(AdminFlags.Event);
}
