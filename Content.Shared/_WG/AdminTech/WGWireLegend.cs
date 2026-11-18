using Content.Shared.Wires;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.AdminTech;

/// <summary>A tool that, held while a wires panel is open, shows its holder what each wire does.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGWireLegendComponent : Component;

/// <summary>Client to server, on a wires panel: tell me what each wire does, if I hold a legend tool.</summary>
[Serializable, NetSerializable]
public sealed class WGWireLegendRequestMessage : BoundUserInterfaceMessage;

/// <summary>Server to the one asking client: what each wire of the panel does.</summary>
[Serializable, NetSerializable]
public sealed class WGWireLegendMessage(List<WGWireLegendEntry> wires) : BoundUserInterfaceMessage
{
    public readonly List<WGWireLegendEntry> Wires = wires;
}

/// <summary>One wire of a legend: how it looks and what it does.</summary>
[Serializable, NetSerializable]
public readonly record struct WGWireLegendEntry(WireColor Color, WireLetter Letter, string Name);
