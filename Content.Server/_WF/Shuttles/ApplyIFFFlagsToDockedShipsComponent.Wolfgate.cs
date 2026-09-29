using Content.Shared.Shuttles.Components;

namespace Content.Server._Mono.Detection;

public sealed partial class ApplyIFFFlagsToDockedShipsComponent
{
    /// <summary>
    /// Flags this host added to each docked ship, so undocking takes back only those.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, IFFFlags> AddedFlags = new();
}
