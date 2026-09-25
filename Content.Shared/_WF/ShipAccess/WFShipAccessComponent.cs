using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipAccess;

/// <summary>
/// Who may open a purchased ship: its owner, the people on its allow list and, in Faction mode, anyone
/// carrying an ID card of the ship's company. Lives on the grid.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFShipAccessComponent : Component
{
    /// <summary>Owner's account. Guid.Empty means no owner yet (a ship bought before this existed).</summary>
    [DataField, AutoNetworkedField]
    public NetUserId OwnerUserId;

    /// <summary>Owner's character name at purchase, for display.</summary>
    [DataField, AutoNetworkedField]
    public string OwnerName = string.Empty;

    /// <summary>Who besides the owner and allow list may enter; see <see cref="WFShipAccessMode"/>.</summary>
    [DataField, AutoNetworkedField]
    public WFShipAccessMode Mode = WFShipAccessMode.Private;

    /// <summary>
    /// People who may open the ship. Ids are networked on purpose so the client predicts its own door opens.
    /// Codes (F3.3) must never be added to a networked field here.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<WFShipAccessEntry> AllowList = new();

    /// <summary>The old "lock ship" toggle. Source of truth: every ShipAccessReaderComponent on the grid mirrors it in Enabled.</summary>
    [DataField, AutoNetworkedField]
    public bool Locked;

    /// <summary>True once an account has been recorded as owner.</summary>
    public bool HasOwner => OwnerUserId.UserId != Guid.Empty;
}

/// <summary>Private admits the owner and the allow list; Faction also admits the ship company's ID cards.</summary>
[Serializable, NetSerializable]
public enum WFShipAccessMode : byte
{
    Private,
    Faction,
}

/// <summary>One person on a ship's allow list.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class WFShipAccessEntry
{
    /// <summary>The listed person's account.</summary>
    [DataField]
    public NetUserId UserId;

    /// <summary>Character name when added, for display.</summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>Shown next to the name, such as "Guest". Stored and shown, not edited in F3.1.</summary>
    [DataField]
    public string Label = string.Empty;

    /// <summary>Stored only in F3.1; a later step lets builders modify the ship.</summary>
    [DataField]
    public bool Builder;
}
