using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>How far an infection has got. Read off one number, so the analyzer and the effects agree.</summary>
[Serializable, NetSerializable]
public enum WolfmedInfectionStage : byte
{
    /// <summary>Contaminated but not yet doing anything.</summary>
    None,

    /// <summary>Local: the wound hurts and keeps reopening.</summary>
    Local,

    /// <summary>Spreading: fever and toxins.</summary>
    Spreading,

    /// <summary>The wound has fed a systemic infection; see <see cref="WolfmedSepsisComponent"/>.</summary>
    Septic,
}

/// <summary>
/// Contamination on one wound. Added by <see cref="WolfmedInfectionSystem"/> to any wound whose prototype
/// declares <see cref="WolfmedInfectionRiskBehavior"/>, and removed when the wound closes or is cured.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedInfectionComponent : Component
{
    /// <summary>0 to the profile's sepsis threshold.</summary>
    [DataField, AutoNetworkedField]
    public float Progress;

    /// <summary>Cached from <see cref="Progress"/> so readers do not need the profile.</summary>
    [DataField, AutoNetworkedField]
    public WolfmedInfectionStage Stage;

    /// <summary>Antiseptic has been on this wound and nothing dirty has happened since.</summary>
    [DataField, AutoNetworkedField]
    public bool Cleaned;

    /// <summary>Extra multiplier from a dirty tool having been in the wound.</summary>
    [DataField]
    public float Contamination = 1f;

    /// <summary>Severity the infection has added back so far, against the profile's cap.</summary>
    [DataField]
    public FixedPoint2 SeverityAdded;
}

/// <summary>
/// A systemic infection. On the body, not on a part: this is the stage that kills, and it outlives the
/// wound that started it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedSepsisComponent : Component
{
    /// <summary>0 to 100. The toxin rate scales with it and it only falls while every source is gone.</summary>
    [DataField, AutoNetworkedField]
    public float Progress;
}

/// <summary>What killed the tissue. Only decides the wording and the analyzer flag.</summary>
[Serializable, NetSerializable]
public enum WolfmedNecrosisSource : byte
{
    Wound,
    Tourniquet,
    Reattachment,
}

/// <summary>
/// Tissue in this part is dying, or has died. Accumulates from whatever source got there first; once
/// <see cref="Necrotic"/> is set the part is finished and only amputation clears it, because the component
/// dies with the part.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedNecrosisComponent : Component
{
    [DataField, AutoNetworkedField]
    public WolfmedNecrosisSource Source;

    /// <summary>Seconds at risk so far.</summary>
    [DataField, AutoNetworkedField]
    public float Progress;

    /// <summary>Seconds at risk the part can take. Zero means nothing is currently killing it.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Onset;

    /// <summary>The patient has already been told this limb is going.</summary>
    [DataField, AutoNetworkedField]
    public bool Warned;

    /// <summary>The part is dead. Permanent: nothing in this system ever clears it.</summary>
    [DataField, AutoNetworkedField]
    public bool Necrotic;

    /// <summary>When the part came off, while it is off. Null on an attached part.</summary>
    [DataField]
    public TimeSpan? DetachedAt;
}

/// <summary>
/// A tourniquet is on this part. The tourniquet item is consumed when it is applied, so the part carries
/// the record; it is what <see cref="WolfmedNecrosisSystem"/> counts minutes against.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedTourniquetComponent : Component
{
    /// <summary>Played at the patient when the strap comes off.</summary>
    [DataField]
    public SoundSpecifier? LoosenSound = new SoundCollectionSpecifier("WolfmedClothUnwrap");
}
