using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.PlanetCracker.Anchors;

/// <summary>
/// One half of a gravity anchor pair: wrenched down on a planet ground layer, drilled in, then locked for the crack.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class WFGravityAnchorComponent : Component
{
    /// <summary>Where this anchor is in its lifecycle; the only writer is WFGravityAnchorSystem.</summary>
    [DataField, AutoNetworkedField]
    public WFAnchorState State = WFAnchorState.Loose;

    /// <summary>The other anchor of this pair, or null when unpaired.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Partner;

    /// <summary>The cracker grid that owns this anchor, so two crackers cannot share a pair.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Cracker;

    /// <summary>When the running drill finishes.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan DrillEnd;

    /// <summary>How long the drill takes, per design section 5.</summary>
    [DataField]
    public TimeSpan DrillDuration = TimeSpan.FromMinutes(5);

    /// <summary>True past the damage threshold; networked now, consumed by F4's crack pause. Gates nothing in F3.</summary>
    [DataField, AutoNetworkedField]
    public bool Damaged;

    /// <summary>Closest two anchor centres may be and still pair, in tiles.</summary>
    [DataField]
    public float MinDistance = 16f;

    /// <summary>Furthest two anchor centres may be and still pair, in tiles.</summary>
    [DataField]
    public float MaxDistance = 40f;

    /// <summary>Tiles added to half the pair distance to get the cut radius (design D21).</summary>
    [DataField]
    public float CutPadding = 2f;

    /// <summary>Damage at which the prototype's Destructible Breakage threshold fires; keep in sync with the YAML.</summary>
    [DataField]
    public float BreakDamage = 300f;

    /// <summary>Fraction of BreakDamage at which the anchor counts as damaged (design section 5: 50%).</summary>
    [DataField]
    public float DamageFraction = 0.5f;

    /// <summary>Half-width of the square footprint that must be free and reserved, in tiles.</summary>
    [DataField]
    public int FootprintRadius = 1;

    /// <summary>Mass this adds to a carrying hull's gravgen load while it rides as cargo (design D11).</summary>
    [DataField]
    public float VirtualMass = 6f;

    /// <summary>
    /// How far the running cut has got, 0 to 1; what the cut circle overlay draws its growing arc from.
    /// It lives on the anchor rather than on the hull because the anchors carry CEPvsOverrideComponent and so replicate
    /// to every viewer on every layer, while the hull grid is four layers above a surface viewer and not in their PVS.
    /// Defaults to 1 so an idle pair still previews the whole circle.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CrackProgress = 1f;
}
