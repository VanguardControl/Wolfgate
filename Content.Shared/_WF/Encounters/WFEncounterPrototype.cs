using System.Numerics;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.NpcCrew;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Encounters;

/// <summary>
/// An encounter: one or more NPC-crewed ships spawned together in open space, each with its side, its rules and
/// its orders.
/// </summary>
[Prototype("wfEncounter")]
public sealed partial class WFEncounterPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Name of the encounter; takes $designation.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Sector announcement when it starts; takes $name. None announces nothing.</summary>
    [DataField]
    public LocId? Announcement;

    /// <summary>Who the announcement is from.</summary>
    [DataField]
    public LocId? AnnouncementSender;

    [DataField(required: true)]
    public List<WFEncounterShip> Ships = new();

    /// <summary>Whether the scheduler may pick it. Off leaves it to admins and other code.</summary>
    [DataField]
    public bool Scheduled = true;

    [DataField]
    public float Weight = 1f;

    [DataField]
    public int MinPlayers;

    /// <summary>Seconds before the scheduler may pick the same encounter again.</summary>
    [DataField]
    public float Cooldown = 1800f;

    /// <summary>Seconds until it ends as expired. Zero never expires.</summary>
    [DataField]
    public float Duration;

    /// <summary>How far from the chosen player ship the scheduler places it.</summary>
    [DataField]
    public float MinDistance = 2500f;

    [DataField]
    public float MaxDistance = 4000f;
}

/// <summary>One ship of an encounter with its crew, its rules and its orders.</summary>
[DataDefinition]
public sealed partial class WFEncounterShip
{
    /// <summary>What the other ships' orders call this one.</summary>
    [DataField(required: true)]
    public string Key = string.Empty;

    [DataField(required: true)]
    public ProtoId<VesselPrototype> Vessel;

    /// <summary>Position relative to the encounter's origin.</summary>
    [DataField]
    public Vector2 Offset;

    /// <summary>Ships of the same side are one battlegroup. The encounter is decided when one side is left.</summary>
    [DataField]
    public string Side = string.Empty;

    [DataField]
    public int Deckhands = 2;

    [DataField]
    public bool Captain = true;

    [DataField]
    public ProtoId<NpcFactionPrototype> Faction = "WFCrew";

    [DataField]
    public ProtoId<CompanyPrototype>? Company;

    [DataField]
    public WFCrewSecurityResponse Boarding = WFCrewSecurityResponse.Hostile;

    [DataField]
    public WFCrewSecurityResponse Docking = WFCrewSecurityResponse.Hostile;

    /// <summary>When this ship stops attacking another.</summary>
    [DataField]
    public WFCrewDisengage Disengage = WFCrewDisengage.Disable;

    [DataField]
    public float DisengageRange = 500f;

    /// <summary>Whether the ship evades attackers.</summary>
    [DataField]
    public bool Evades = true;

    /// <summary>Flight limits; none uses the cautious defaults.</summary>
    [DataField]
    public ProtoId<WFCrewNavigationProfilePrototype>? Navigation;

    /// <summary>Tasks flown in order.</summary>
    [DataField]
    public List<WFEncounterObjective> Objectives = new();
}

/// <summary>One task of an encounter ship's queue.</summary>
[DataDefinition]
public sealed partial class WFEncounterObjective
{
    [DataField(required: true)]
    public WFCrewObjectiveKind Kind;

    /// <summary>Key of the ship the task is aimed at.</summary>
    [DataField]
    public string? Target;

    /// <summary>GoTo: the point, relative to the encounter's origin.</summary>
    [DataField]
    public Vector2 Offset;

    [DataField]
    public float Range = 100f;

    /// <summary>Seconds; zero is until done or forever, by kind.</summary>
    [DataField]
    public float Duration;
}

/// <summary>How an encounter ended.</summary>
public enum WFEncounterResolution : byte
{
    /// <summary>Its duration ran out.</summary>
    Expired,

    /// <summary>Every ship flew its orders to the end.</summary>
    Completed,

    /// <summary>One side is left in the fight.</summary>
    Decided,

    /// <summary>No ship is left in the fight.</summary>
    Destroyed,

    /// <summary>An admin or the round ended it.</summary>
    Ended,
}
