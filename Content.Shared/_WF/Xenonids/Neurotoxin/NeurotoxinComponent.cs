// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Neurotoxin;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NeurotoxinComponent : Component
{
    [DataField, AutoNetworkedField]
    public float NeurotoxinAmount = 0;

    [DataField, AutoNetworkedField]
    public TimeSpan LastMessage;

    [DataField, AutoNetworkedField]
    public TimeSpan LastStumbleTime;

    [DataField, AutoNetworkedField]
    public TimeSpan LastAccentTime;

    [DataField, AutoNetworkedField]
    public DamageSpecifier ToxinDamage = new();

    [DataField, AutoNetworkedField]
    public DamageSpecifier OxygenDamage = new();

    [DataField, AutoNetworkedField]
    public DamageSpecifier CoughDamage = new();
}
