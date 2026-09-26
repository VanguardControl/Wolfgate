using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipAccess;

/// <summary>
/// Who may open a purchased ship: whoever carries its deed, the ID cards on its allow list and, in Faction
/// mode, anyone carrying an ID card of the ship's company. Lives on the grid. The allow list holds cards, not
/// people, like the deed itself and Mono's guest cards: whoever carries a listed card gets in.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFShipAccessComponent : Component
{
    /// <summary>The buyer's character name at purchase, shown on the tab. Grants nothing; the deed card does.</summary>
    [DataField, AutoNetworkedField]
    public string OwnerName = string.Empty;

    /// <summary>Who besides the deed and the allow list may enter; see <see cref="WFShipAccessMode"/>.</summary>
    [DataField, AutoNetworkedField]
    public WFShipAccessMode Mode = WFShipAccessMode.Private;

    /// <summary>
    /// ID cards that may open the ship. Round state like guest cards, so not saved with the grid. Networked on
    /// purpose so the client predicts its own door opens. Codes (F3.3) must never be added to a networked field here.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public List<WFShipAccessEntry> AllowList = new();

    /// <summary>The old "lock ship" toggle. Source of truth: every ShipAccessReaderComponent on the grid mirrors it in Enabled.</summary>
    [DataField, AutoNetworkedField]
    public bool Locked;
}

/// <summary>Private admits the deed and the allow list; Faction also admits the ship company's ID cards.</summary>
[Serializable, NetSerializable]
public enum WFShipAccessMode : byte
{
    Private,
    Faction,
}

/// <summary>One ID card on a ship's allow list.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessEntry
{
    /// <summary>The card itself; the key the readers compare against.</summary>
    public NetEntity Card;

    /// <summary>The name on the card when it was added, or its holder's, for display.</summary>
    public string Name = string.Empty;

    /// <summary>Shown next to the name, such as "Guest". Stored and shown, not edited in F3.1.</summary>
    public string Label = string.Empty;

    /// <summary>Stored only in F3.1; a later step lets builders modify the ship.</summary>
    public bool Builder;
}
