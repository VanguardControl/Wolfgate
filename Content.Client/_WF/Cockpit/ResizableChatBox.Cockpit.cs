namespace Content.Client.UserInterface.Systems.Chat.Widgets;

public sealed partial class ResizableChatBox
{
    /// <summary>The cockpit owns this chat's bounds while retaining its input and history.</summary>
    public bool WfCockpitDocked { get; set; }
}
