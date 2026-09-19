using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Flight;

/// <summary>
/// Sent by the shuttle console when the pilot asks to drop out of orbit into the planet's atmosphere. A hull that
/// cannot hold itself up has to send this twice: the first is refused with the lift ratio, the second is the confirm.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFEnterAtmosphereMessage : BoundUserInterfaceMessage
{
    /// <summary>The console the request came from; the server re-resolves the hull from it.</summary>
    public NetEntity Console;

    /// <summary>True once the pilot has answered the lift warning, which is the only thing that lets a sinker through.</summary>
    public bool Confirmed;

    public WFEnterAtmosphereMessage(NetEntity console, bool confirmed)
    {
        Console = console;
        Confirmed = confirmed;
    }
}
