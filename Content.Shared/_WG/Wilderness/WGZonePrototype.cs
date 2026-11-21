using Content.Shared.Alert;
using Content.Shared.Guidebook;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WG.Wilderness;

/// <summary>A kind of zone the rules give a protection status to, and how it is shown to players inside it.</summary>
[Prototype("wgZone")]
public sealed partial class WGZonePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public WGZoneKind Kind;

    /// <summary>Where zones overlap, the highest wins.</summary>
    [DataField]
    public int Priority;

    /// <summary>Metres past the source's edge the zone reaches, unless the source sets its own.</summary>
    [DataField]
    public float Radius;

    [DataField]
    public Color Color = Color.White;

    /// <summary>The banner's title, such as "WILDERNESS".</summary>
    [DataField(required: true)]
    public LocId Title;

    /// <summary>The banner's line under the title; given <c>open</c> (anyone may fight) and <c>hot</c> (war declared).</summary>
    [DataField(required: true)]
    public LocId Summary;

    /// <summary>Title and summary while the war is HOT, for zones whose protection depends on it.</summary>
    [DataField]
    public LocId? HotTitle;

    [DataField]
    public LocId? HotSummary;

    /// <summary>Said in chat on entering and leaving.</summary>
    [DataField(required: true)]
    public LocId Entered;

    [DataField(required: true)]
    public LocId Left;

    /// <summary>The name its rings carry on the radar.</summary>
    [DataField(required: true)]
    public LocId RingLabel;

    [DataField]
    public ProtoId<AlertPrototype>? Alert;

    [DataField]
    public SpriteSpecifier? Icon;

    [DataField]
    public SoundSpecifier? EnterSound;

    /// <summary>The rule the banner opens when clicked.</summary>
    [DataField]
    public ProtoId<GuideEntryPrototype>? Guide;

    /// <summary>Whether staying in the zone marks a ship or person (<see cref="WGSkullComponent"/>).</summary>
    [DataField]
    public bool Skulls;

    /// <summary>Whether the screen-edge border shows while inside.</summary>
    [DataField]
    public bool Border;

    /// <summary>Whether radars draw the zone's edge.</summary>
    [DataField]
    public bool Rings = true;
}

/// <summary>What a zone means for the people in it.</summary>
public enum WGZoneKind : byte
{
    /// <summary>Policy protects everyone not causing trouble.</summary>
    Safe,

    /// <summary>Fighting needs no escalation; staying marks you.</summary>
    Wilderness,

    /// <summary>A faction outpost, protected from port strikes while the war is COLD.</summary>
    Outpost,

    /// <summary>Named as not a safe zone; ordinary rules.</summary>
    NotSafe,
}
