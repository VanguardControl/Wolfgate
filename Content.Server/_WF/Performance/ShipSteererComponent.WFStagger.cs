using Content.Server.Physics.Controllers;

namespace Content.Server._Mono.NPC.HTN;

public sealed partial class ShipSteererComponent
{
    /// <summary>What the steering worked out last, reused on the ticks between its turns.</summary>
    [ViewVariables]
    public ShuttleInput? WFLastInput;
}
