using Robust.Shared.Prototypes;

namespace Content.Shared._WG.AHelpTickets;

/// <summary>
/// A canned ahelp reply staff pick from the ticket panel. Hosts and Owners own the list
/// (Resources/Prototypes/_WG/AHelpTickets/macros.yml and its Fluent file).
/// </summary>
[Prototype("wgAHelpMacro")]
public sealed partial class WGAHelpMacroPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The menu entry.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>What the player is sent. Takes $player (their character name).</summary>
    [DataField]
    public LocId? Text;

    /// <summary>What happens to the ticket after the reply.</summary>
    [DataField]
    public AHelpTicketAction? Action;

    /// <summary>
    /// A console command run after the reply, with {username} and {entity} replaced by the player's account name
    /// and body. The console checks the staffer's permissions as usual.
    /// </summary>
    [DataField]
    public string? Command;

    /// <summary>Lower comes first in the menu.</summary>
    [DataField]
    public int Order;
}
