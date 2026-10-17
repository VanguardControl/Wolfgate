using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._WF.GodPanel;

/// <summary>Everything the panel can ask the server to do to a player, a grid or the round.</summary>
[Serializable, NetSerializable]
public enum WFGodAction : byte
{
    // Player
    Follow,
    GoTo,
    Bring,
    SecondChance,
    OfferToGhosts,
    Rejuvenate,

    // Grid
    PowerOn,
    PowerOff,
    Announce,
    SetCode,
    GeneralQuartersOn,
    GeneralQuartersOff,
    DistressOn,
    DistressOff,
    HailAsGod,
    PlayPaSound,
    StopPaSound,
    EndEncounter,

    // Round
    PlaySound,
    StopSound,
    PauseEncounters,
    ResumeEncounters,
    ReaverPause,
    ReaverResume,
    WarLevelCold,
    WarLevelHot,
}

/// <summary>The GM opens or closes the panel; only subscribed admins get state.</summary>
[Serializable, NetSerializable]
public sealed class WFGodPanelSubscribeEvent : EntityEventArgs
{
    public bool Subscribe;

    public WFGodPanelSubscribeEvent(bool subscribe)
    {
        Subscribe = subscribe;
    }
}

/// <summary>The panel's state, sent about every <c>wf.godpanel.update_interval</c> seconds.</summary>
[Serializable, NetSerializable]
public sealed class WFGodPanelStateEvent : EntityEventArgs
{
    public WFGodPanelState State;

    public WFGodPanelStateEvent(WFGodPanelState state)
    {
        State = state;
    }
}

/// <summary>One action. <c>Text</c> is the announcement, code id or link; <c>Flag</c> means "with the announcement" for power.</summary>
[Serializable, NetSerializable]
public sealed class WFGodPanelActionEvent : EntityEventArgs
{
    public WFGodAction Action;
    public NetEntity? Target;
    public string? Text;
    public bool Flag;

    public WFGodPanelActionEvent(WFGodAction action, NetEntity? target, string? text, bool flag)
    {
        Action = action;
        Target = target;
        Text = text;
        Flag = flag;
    }
}

/// <summary>Asks for a feed on a target; a null target is a free camera at the GM's position.</summary>
[Serializable, NetSerializable]
public sealed class WFGodFeedOpenEvent : EntityEventArgs
{
    public NetEntity? Target;

    public WFGodFeedOpenEvent(NetEntity? target)
    {
        Target = target;
    }
}

/// <summary>The feed is open: the camera entity to look through, and the tick to start the eye at.</summary>
[Serializable, NetSerializable]
public sealed class WFGodFeedOpenedEvent : EntityEventArgs
{
    public int FeedId;
    public NetEntity Camera;
    public NetEntity? Target;
    public GameTick Tick;

    public WFGodFeedOpenedEvent(int feedId, NetEntity camera, NetEntity? target, GameTick tick)
    {
        FeedId = feedId;
        Camera = camera;
        Target = target;
        Tick = tick;
    }
}

/// <summary>The GM closes a feed.</summary>
[Serializable, NetSerializable]
public sealed class WFGodFeedCloseEvent : EntityEventArgs
{
    public int FeedId;

    public WFGodFeedCloseEvent(int feedId)
    {
        FeedId = feedId;
    }
}

/// <summary>A feed is gone: closed, its target deleted, or the panel unsubscribed.</summary>
[Serializable, NetSerializable]
public sealed class WFGodFeedClosedEvent : EntityEventArgs
{
    public int FeedId;

    public WFGodFeedClosedEvent(int feedId)
    {
        FeedId = feedId;
    }
}

/// <summary>Moves a free camera.</summary>
[Serializable, NetSerializable]
public sealed class WFGodFeedMoveEvent : EntityEventArgs
{
    public int FeedId;
    public NetCoordinates Coordinates;

    public WFGodFeedMoveEvent(int feedId, NetCoordinates coordinates)
    {
        FeedId = feedId;
        Coordinates = coordinates;
    }
}
