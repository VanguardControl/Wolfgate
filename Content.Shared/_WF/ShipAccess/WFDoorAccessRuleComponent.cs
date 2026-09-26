using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipAccess;

/// <summary>
/// A door's own access rule, overriding the ship-wide one. Serialises with the grid. Doors only; lockers
/// keep the ship rule. Codes never live here: <see cref="HasOwnCode"/> only says the server holds one.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFDoorAccessRuleComponent : Component
{
    /// <summary>Who may open this door; see <see cref="WFDoorAccessRule"/>.</summary>
    [DataField, AutoNetworkedField]
    public WFDoorAccessRule Rule = WFDoorAccessRule.Default;

    /// <summary>
    /// ID cards picked for this door under Players and PlayersOrCode. Each is on the ship's allow list when
    /// added and is dropped from here when it leaves that list. Round state like the allow list, so not saved
    /// with the grid. Networked so the client predicts its own opens.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public HashSet<EntityUid> Players = new();

    /// <summary>True while the server holds a code for this door, so the console can show that without the code.</summary>
    [DataField, AutoNetworkedField]
    public bool HasOwnCode;

    /// <summary>Whether sealing added the door's bolt component, so unsealing can take it away again.</summary>
    [DataField]
    public bool AddedBolt;

    /// <summary>Set when a seal was lifted while the door had no power; bolts need power, so they come up once it returns.</summary>
    [DataField]
    public bool UnboltWhenPowered;
}

/// <summary>The seven per-door rules from the design table. Every rule but Default applies whether or not the ship is locked.</summary>
[Serializable, NetSerializable]
public enum WFDoorAccessRule : byte
{
    /// <summary>Follows the ship: deed, allow list and faction cards, and only while the ship is locked.</summary>
    Default,

    /// <summary>The deed holder only.</summary>
    OwnerOnly,

    /// <summary>The deed holder and the cards picked for this door.</summary>
    Players,

    /// <summary>The deed holder, and anyone who enters the door's code or the ship code at the keypad.</summary>
    Code,

    /// <summary>The deed holder, the cards picked for this door, or a code.</summary>
    PlayersOrCode,

    /// <summary>Everyone.</summary>
    Public,

    /// <summary>Nobody, bolted shut; the deed holder unseals it from the console.</summary>
    Sealed,
}
