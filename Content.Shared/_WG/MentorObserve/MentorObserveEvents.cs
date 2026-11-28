using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.MentorObserve;

/// <summary>How a mentor is with the asker.</summary>
public enum MentorVisitKind : byte
{
    /// <summary>Watching: a ghost bound to the asker, or a living mentor stepped out into an observer.</summary>
    Watch,

    /// <summary>In the round as a mentor critter.</summary>
    Critter,
}

/// <summary>Client to server: a responder asks to watch the asker of a ticket, or to join them as a critter.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorVisitRequestEvent : EntityEventArgs
{
    public int TicketId;
    public MentorVisitKind Kind;

    /// <summary>For a critter: the <c>wgMentorCritter</c> prototype picked.</summary>
    public string? Critter;
}

/// <summary>Server to the asker: a mentor asks to watch or join them. Answered with <see cref="WGMentorVisitAnswerEvent"/>.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorVisitPromptEvent : EntityEventArgs
{
    public int TicketId;
    public MentorVisitKind Kind;

    /// <summary>For a critter: the animal's name, already localised.</summary>
    public string Animal = "";
}

/// <summary>Client to server: the asker allows or refuses a pending visit.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorVisitAnswerEvent : EntityEventArgs
{
    public int TicketId;
    public bool Allow;
}

/// <summary>Client to server: the asker ends every visit to them, or takes the critter out of their pocket or off their head.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorVisitEndEvent : EntityEventArgs
{
    /// <summary>Only take the critter out, leaving it with them.</summary>
    public bool TakeOutOnly;
}

/// <summary>Client to server: a mentor goes back to their body, or a ghost mentor stops following (Shift+F3, the action, the button).</summary>
[Serializable, NetSerializable]
public sealed class WGMentorReturnRequestEvent : EntityEventArgs
{
}

/// <summary>Server to one client: the visits that concern them, for the Mentor Help window.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorVisitStateEvent : EntityEventArgs
{
    /// <summary>As the asker: a mentor is watching them.</summary>
    public bool Watched;

    /// <summary>As the asker: a critter is with them, and whether it rides in a pocket or on their head.</summary>
    public bool CritterWithMe;

    public bool CritterRiding;

    /// <summary>As a mentor: the ticket they are visiting, and how.</summary>
    public int? VisitingTicket;

    public MentorVisitKind VisitingKind;

    /// <summary>As a mentor: watching as a ghost bound to the asker, rather than stepped out.</summary>
    public bool VisitingAsGhost;

    /// <summary>As a mentor: tickets waiting for the asker's answer to them.</summary>
    public List<int> Pending = new();
}

/// <summary>Server to the asker: a mentor critter points something out; only they see it.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorPingEvent : EntityEventArgs
{
    public NetCoordinates Coordinates;
}

/// <summary>Limits for watching and critters.</summary>
public static class MentorObserveRules
{
    /// <summary>How long after taking damage a mentor may not step out.</summary>
    public static readonly TimeSpan RecentDamage = TimeSpan.FromSeconds(60);

    /// <summary>Wait between step-outs.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    /// <summary>How long a ping marker shows.</summary>
    public static readonly TimeSpan PingTime = TimeSpan.FromSeconds(4);
}
