using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Lathe;

/// <summary>
/// A dedicated store for one class of lathe inputs. Reagents are kept as separate
/// amounts so chemicals in the silo cannot react with each other.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FabricationSiloComponent : Component
{
    [DataField, AutoNetworkedField]
    public FabricationSiloKind Kind;

    [DataField, AutoNetworkedField]
    public float Range = 125f;

    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> Clients = new();

    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Reagents = new();

    public Container Parts = default!;
}

public enum FabricationSiloKind : byte
{
    Parts,
    Chemicals,
}

/// <summary>
/// Independent links allow a lathe to use ore, parts, and chemicals at once.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FabricationSiloClientComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? PartsSilo;

    [DataField, AutoNetworkedField]
    public EntityUid? ChemicalSilo;
}

[Serializable, NetSerializable]
public enum FabricationSiloUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class FabricationSiloBuiState : BoundUserInterfaceState
{
    public readonly FabricationSiloKind Kind;
    public readonly List<(NetEntity Entity, string Label)> Clients;
    public readonly List<(NetEntity? Entity, string Label)> Stock;

    public FabricationSiloBuiState(
        FabricationSiloKind kind,
        List<(NetEntity Entity, string Label)> clients,
        List<(NetEntity? Entity, string Label)> stock)
    {
        Kind = kind;
        Clients = clients;
        Stock = stock;
    }
}

[Serializable, NetSerializable]
public sealed class ToggleFabricationSiloClientMessage(NetEntity client) : BoundUserInterfaceMessage
{
    public readonly NetEntity Client = client;
}

[Serializable, NetSerializable]
public sealed class EjectFabricationSiloPartMessage(NetEntity part) : BoundUserInterfaceMessage
{
    public readonly NetEntity Part = part;
}
