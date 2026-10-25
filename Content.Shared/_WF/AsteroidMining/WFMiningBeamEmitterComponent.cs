using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>
/// A ship-mounted mining laser gun. Aimed and fired from a gunnery console; each pulse that hits an asteroid keeps it
/// heating at the power set on a mining beam console.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFMiningBeamEmitterComponent : Component
{
    /// <summary>Reach of the beam, matching its pulse's raycast; shown on the mining console.</summary>
    [DataField] public float MaxRange = 60f;

    /// <summary>Heat delivered at full power, before the rock's resistance.</summary>
    [DataField] public float Strength = 1.15f;

    /// <summary>Scales the width of every rock's cracking window.</summary>
    [DataField] public float WindowScale = 1f;

    /// <summary>Seconds the gun can't fire after its target blows up.</summary>
    [DataField] public float OverloadCooldown = 10f;

    /// <summary>The beam stays lit this long after the last pulse.</summary>
    [DataField] public float ActiveTime = 0.3f;

    /// <summary>Where the beam leaves the barrel, in the gun's local frame.</summary>
    [DataField] public Vector2 MuzzleOffset = new(0f, -1.31f);

    /// <summary>The barrel's direction in the gun's local frame; Mono guns face south.</summary>
    [DataField] public Vector2 Forward = new(0f, -1f);

    [DataField] public Color BeamColor = Color.FromHex("#ffb347");

    [DataField] public SoundSpecifier? LoopSound = new SoundPathSpecifier("/Audio/Ambience/Objects/gravity_gen_hum.ogg",
        AudioParams.Default.WithVolume(-2f).WithLoop(true).WithMaxDistance(20f));

    /// <summary>The gun is firing; the beam ends on <see cref="TargetGrid"/> or, with none, <see cref="BeamLength"/> out.</summary>
    [AutoNetworkedField] public bool Active;
    [AutoNetworkedField] public EntityUid? TargetGrid;
    [AutoNetworkedField] public Vector2 TargetCenter;
    [AutoNetworkedField] public float TargetRadius;
    [AutoNetworkedField] public float BeamLength;

    /// <summary>Power setting from zero to one.</summary>
    [AutoNetworkedField] public float Power = 0.5f;

    /// <summary>Target heat for the beam's colour and impact glow.</summary>
    [AutoNetworkedField] public float Charge;

    // Runtime state, never saved into maps.
    public EntityUid? Target;
    public EntityUid? Audio;
    public TimeSpan CooldownUntil;
    public TimeSpan LastPulse;
    public float Contribution;
}

[Serializable, NetSerializable]
public enum WFMiningBeamVisualLayers : byte
{
    Base,
}
