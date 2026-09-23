using Content.Shared._WF.Construction;

namespace Content.Client._WF.Construction;

/// <summary>
/// Sends the construction menu's repeat-craft requests.
/// </summary>
public sealed class CraftRepeatSystem : EntitySystem
{
    public void Request(string prototype, int count)
    {
        RaiseNetworkEvent(new CraftRepeatRequestEvent(prototype, count));
    }
}
