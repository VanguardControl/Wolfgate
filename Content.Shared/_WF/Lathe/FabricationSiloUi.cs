using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Lathe;

/// <summary>UI key of the fabrication silo window.</summary>
[Serializable, NetSerializable]
public enum FabricationSiloUiKey : byte
{
    Key,
}

/// <summary>
/// Silo window state: machines in range and stored supplies.
/// </summary>
[Serializable, NetSerializable]
public sealed class FabricationSiloBuiState(
    FabricationSiloKind kind,
    float range,
    List<FabricationSiloClientEntry> clients,
    List<FabricationSiloStockEntry> stock) : BoundUserInterfaceState
{
    /// <summary>
    /// The supply the silo holds.
    /// </summary>
    public readonly FabricationSiloKind Kind = kind;

    /// <summary>
    /// Furthest a linked machine can be.
    /// </summary>
    public readonly float Range = range;

    /// <summary>
    /// Machines in range, plus any linked machine.
    /// </summary>
    public readonly List<FabricationSiloClientEntry> Clients = clients;

    /// <summary>
    /// Stored supplies, one entry per kind of part or reagent.
    /// </summary>
    public readonly List<FabricationSiloStockEntry> Stock = stock;
}

/// <summary>
/// A machine the silo can supply; Beacon is null for a linked machine outside the lookup.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct FabricationSiloClientEntry(
    NetEntity Entity,
    string Name,
    string? Beacon,
    bool Linked,
    bool Available);

/// <summary>
/// One stored supply; Entity is set for parts so the stack can be ejected.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct FabricationSiloStockEntry(NetEntity? Entity, string Name, FixedPoint2 Amount);

/// <summary>
/// Links or unlinks a machine.
/// </summary>
[Serializable, NetSerializable]
public sealed class ToggleFabricationSiloClientMessage(NetEntity client) : BoundUserInterfaceMessage
{
    /// <summary>
    /// The machine to link or unlink.
    /// </summary>
    public readonly NetEntity Client = client;
}

/// <summary>
/// Ejects one stored part or stack.
/// </summary>
[Serializable, NetSerializable]
public sealed class EjectFabricationSiloPartMessage(NetEntity part) : BoundUserInterfaceMessage
{
    /// <summary>
    /// The stored part to eject.
    /// </summary>
    public readonly NetEntity Part = part;
}
