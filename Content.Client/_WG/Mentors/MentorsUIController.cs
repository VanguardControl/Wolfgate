using Content.Client.UserInterface.Systems.Chat;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._WG.Mentors;

/// <summary>Tells the chat box to work out its channels again when this player's mentor status changes.</summary>
[UsedImplicitly]
public sealed class MentorsUIController : UIController, IOnSystemChanged<WGClientMentorSystem>
{
    public void OnSystemLoaded(WGClientMentorSystem system)
    {
        system.StatusChanged += OnStatusChanged;
    }

    public void OnSystemUnloaded(WGClientMentorSystem system)
    {
        system.StatusChanged -= OnStatusChanged;
    }

    private void OnStatusChanged()
    {
        UIManager.GetUIController<ChatUIController>().WGRefreshChannelPermissions();
    }
}
