using Content.Client.Administration.Managers;
using Content.Shared.Chat;

namespace Content.Client._WG.Administration.AdminFollow;

/// <summary>Puts a follow link in front of each admin alert that names an entity, for an active admin.</summary>
public sealed class AdminFollowLinkSystem : EntitySystem
{
    [Dependency] private IClientAdminManager _admin = default!;

    /// <summary>Prefixes the follow link when the message is an admin alert about an entity.</summary>
    public void AddChatLink(ChatMessage msg)
    {
        if (msg.Channel != ChatChannel.AdminAlert || !msg.SenderEntity.Valid || !_admin.IsActive())
            return;

        msg.WrappedMessage = $"[{AdminFollowLinkTag.TagName}={msg.SenderEntity.Id}/] {msg.WrappedMessage}";
    }
}
