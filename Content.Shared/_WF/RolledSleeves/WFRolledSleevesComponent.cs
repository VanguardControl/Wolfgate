using Robust.Shared.GameStates;

namespace Content.Shared._WF.RolledSleeves;

/// <summary>
/// A jumpsuit whose sleeves can be rolled up to the elbow. The server adds it to all inner clothing at map init.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(WFRolledSleevesSystem))]
public sealed partial class WFRolledSleevesComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Rolled;
}
