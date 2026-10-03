using Robust.Shared.Serialization;

namespace Content.Shared._WF.Encounters;

[Serializable, NetSerializable]
public enum WFEncounterAdminAction : byte
{
    /// <summary>Only asks for the current state.</summary>
    Refresh,

    /// <summary>Starts a prototype north of the admin.</summary>
    Spawn,

    /// <summary>Marks an encounter as over; its ships stay until players leave them.</summary>
    Resolve,

    /// <summary>Ends an encounter and removes its ships now.</summary>
    End,

    /// <summary>Has the scheduler pick and start one now.</summary>
    Schedule,

    /// <summary>Applies the scheduler settings in the request.</summary>
    Scheduler,

    /// <summary>Moves the admin to an encounter or one of its ships.</summary>
    Teleport,
}

/// <summary>An admin's request from the encounter window. Every request is answered with the current state.</summary>
[Serializable, NetSerializable]
public sealed class WFEncounterAdminRequest : EntityEventArgs
{
    public WFEncounterAdminAction Action;
    public string Prototype = string.Empty;

    /// <summary>The encounter, or for a teleport the encounter or ship.</summary>
    public NetEntity? Target;
    public float Distance = 300f;
    public bool Enabled;
    public bool Paused;
    public float IntervalMin;
    public float IntervalMax;
    public int MaxActive;
}

/// <summary>Everything the encounter window shows.</summary>
[Serializable, NetSerializable]
public sealed class WFEncounterAdminState : EntityEventArgs
{
    /// <summary>Why the request did nothing, if it did nothing.</summary>
    public string Message = string.Empty;
    public List<WFEncounterAdminEntry> Encounters = new();
    public List<WFEncounterAdminPrototype> Prototypes = new();
    public bool Enabled;
    public bool Paused;
    public float IntervalMin;
    public float IntervalMax;
    public int MaxActive;

    /// <summary>Seconds until the scheduler next tries, or negative while it is not running.</summary>
    public float NextIn = -1f;
}

[Serializable, NetSerializable]
public sealed class WFEncounterAdminEntry
{
    public NetEntity Uid;
    public string Prototype = string.Empty;
    public string Name = string.Empty;
    public string State = string.Empty;
    public bool Resolved;
    public float Age;

    /// <summary>Seconds until it expires, or negative if it never does.</summary>
    public float ExpiresIn = -1f;
    public List<WFEncounterAdminShip> Ships = new();
}

[Serializable, NetSerializable]
public sealed class WFEncounterAdminShip
{
    public string Key = string.Empty;
    public NetEntity Grid;
    public string Name = string.Empty;
    public string Group = string.Empty;
    public string Side = string.Empty;
    public bool Exists;
    public bool Disabled;
    public int Crew;
    public string Activity = string.Empty;
}

[Serializable, NetSerializable]
public sealed class WFEncounterAdminPrototype
{
    public string Id = string.Empty;
    public bool Scheduled;
    public float Weight;
    public bool Running;
}
