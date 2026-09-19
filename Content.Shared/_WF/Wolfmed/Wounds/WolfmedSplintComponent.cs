using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// A rigid support strapped over a broken limb. Field treatment for a fracture: it sets the break to
/// <see cref="FractureTreatment.Reduced"/> without an incision, which is a quarter of the penalty, and
/// leaves the mending to bone gel or Osteogen.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedSplintComponent : Component
{
    /// <summary>How long strapping it on takes on someone else.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Delay = TimeSpan.FromSeconds(4);

    /// <summary>Doing it one-handed, on a limb that is already broken, takes longer.</summary>
    [DataField, AutoNetworkedField]
    public float SelfMultiplier = 2.5f;

    /// <summary>
    /// Part types a splint can be strapped around. A torso or a head has nothing to immobilise, so those
    /// fractures stay surgical.
    /// </summary>
    [DataField]
    public HashSet<BodyPartType> Parts = [BodyPartType.Arm, BodyPartType.Hand, BodyPartType.Leg, BodyPartType.Foot];

    /// <summary>Whether the splint is used up. False leaves a reusable frame for future content.</summary>
    [DataField]
    public bool Consumed = true;

    [DataField]
    public SoundSpecifier? BeginSound;

    [DataField]
    public SoundSpecifier? EndSound;
}

/// <summary>Why a splint refused, so the popup can say something useful.</summary>
public enum WolfmedSplintRefusal : byte
{
    None,
    NoPart,
    WrongPart,
    NoFracture,
    TooSlight,
    AlreadyTreated,
}

[Serializable, NetSerializable]
public sealed partial class WolfmedSplintDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Part;

    public WolfmedSplintDoAfterEvent(NetEntity part)
    {
        Part = part;
    }
}
