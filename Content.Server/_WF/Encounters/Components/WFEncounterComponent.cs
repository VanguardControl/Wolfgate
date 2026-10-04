using Content.Shared._WF.Encounters;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Encounters.Components;

/// <summary>A running encounter. Sits on its own entity at the encounter's origin and owns the ships.</summary>
[RegisterComponent]
public sealed partial class WFEncounterComponent : Component
{
    [DataField]
    public ProtoId<WFEncounterPrototype> Prototype;

    /// <summary>The encounter's name with its designation filled in.</summary>
    [DataField]
    public string Name = string.Empty;

    [DataField]
    public MapCoordinates Origin;

    /// <summary>The ships by their prototype key. A deleted ship keeps its entry.</summary>
    [DataField]
    public Dictionary<string, WFEncounterShipState> Ships = new();

    [DataField]
    public TimeSpan Started;

    /// <summary>When it expires, if it does.</summary>
    [DataField]
    public TimeSpan? Expires;

    /// <summary>Set once resolved; the ships are then removed as players leave them.</summary>
    [DataField]
    public WFEncounterResolution? Resolution;

    [DataField]
    public WFEncounterCategory Category;

    [DataField]
    public int Cost;

    [DataField]
    public WFEncounterLifetime Lifetime;

    /// <summary>Whether it is kept off the sector markers.</summary>
    [DataField]
    public bool Hidden;

    /// <summary>The stations its placement chose, in the order a route calls at them.</summary>
    [DataField]
    public List<EntityUid> Stops = new();

    [DataField]
    public bool AnnounceOnRadio;

    /// <summary>The distance a player must come within before the ships get their orders; zero for at once.</summary>
    [DataField]
    public float StartRadius;

    /// <summary>Whether the ships have their orders.</summary>
    [DataField]
    public bool Begun;

    /// <summary>The announcement still to be made, if any.</summary>
    [DataField]
    public string? Announcement;

    [DataField]
    public string? AnnouncementSender;

    /// <summary>Ship weapon hits by each player's ship on the ships of each side.</summary>
    public Dictionary<Robust.Shared.Network.NetUserId, Dictionary<string, int>> Hits = new();

    /// <summary>The same count per attacking ship.</summary>
    public Dictionary<(EntityUid Attacker, string Side), int> ShipHits = new();

    /// <summary>Player ships a side has taken as allies for the encounter.</summary>
    public List<(EntityUid Ship, EntityUid Ally)> Allies = new();

    /// <summary>When its ships jump out, whoever is near.</summary>
    [DataField]
    public TimeSpan? JumpAt;
}

/// <summary>One ship of a running encounter.</summary>
[DataDefinition]
public sealed partial class WFEncounterShipState
{
    [DataField]
    public EntityUid Grid;

    [DataField]
    public string Group = string.Empty;

    [DataField]
    public string Side = string.Empty;

    /// <summary>Whether its prototype gave it orders, so an empty queue means they are done.</summary>
    [DataField]
    public bool HasOrders;

    [DataField]
    public float WarnRange;

    [DataField]
    public float AttackRange;

    [DataField]
    public string ZoneLines = "wf-encounter-zone";

    [DataField]
    public WFEncounterZoneTargets ZoneTargets;

    /// <summary>Since when no player has been near it, while resolved.</summary>
    public TimeSpan? Quiet;

    /// <summary>Whether it hunts player ships to dock with and board.</summary>
    [DataField]
    public bool Hunt;

    /// <summary>The ship it is trying to dock with.</summary>
    [DataField]
    public EntityUid? Prey;

    /// <summary>Whether it calls for help when adrift.</summary>
    [DataField]
    public bool Distress = true;

    /// <summary>While docked to its prey: when the boarding party is called back.</summary>
    public TimeSpan? RaidEnds;

    /// <summary>The boarding party and the posts they go back to.</summary>
    public Dictionary<EntityUid, Robust.Shared.Map.EntityCoordinates?> BoardingParty = new();

    /// <summary>Whether its raid is over and it is leaving.</summary>
    [DataField]
    public bool Raided;

    /// <summary>Since when the ship has had no thrust while not in a fight.</summary>
    public TimeSpan? AdriftSince;

    /// <summary>When it may next call for help.</summary>
    public TimeSpan NextDistress;

    /// <summary>When its crew next top up its batteries.</summary>
    public TimeSpan NextPower;

    /// <summary>Intruders already warned, and when each may be warned again.</summary>
    public Dictionary<EntityUid, TimeSpan> Warned = new();

    /// <summary>Intruders inside the attack zone that have been told so.</summary>
    public HashSet<EntityUid> Engaged = new();
}
