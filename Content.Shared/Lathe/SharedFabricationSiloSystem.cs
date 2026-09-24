using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Shared.Lathe;

/// <summary>
/// Read-only access to remote lathe supplies. Every resource type has its own link.
/// </summary>
public abstract class SharedFabricationSiloSystem : EntitySystem
{
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public bool CanTransmit(EntityUid silo, EntityUid client, FabricationSiloKind kind)
    {
        if (!TryComp<FabricationSiloComponent>(silo, out var store) || store.Kind != kind)
            return false;

        return _power.IsPowered(silo)
            && _transform.GetGrid(silo) == _transform.GetGrid(client)
            && _transform.InRange(silo, client, store.Range);
    }

    public EntityUid? GetLinkedSilo(EntityUid client, FabricationSiloKind kind)
    {
        if (!TryComp<FabricationSiloClientComponent>(client, out var link))
            return null;

        var silo = kind == FabricationSiloKind.Parts ? link.PartsSilo : link.ChemicalSilo;
        return silo is { } uid && CanTransmit(uid, client, kind) ? uid : null;
    }

    public int GetPartAmount(EntityUid client, EntProtoId prototype)
    {
        if (GetLinkedSilo(client, FabricationSiloKind.Parts) is not { } silo ||
            !TryComp<FabricationSiloComponent>(silo, out var comp) || comp.Parts == null)
            return 0;

        var count = 0;
        foreach (var part in comp.Parts.ContainedEntities)
        {
            if (MetaData(part).EntityPrototype?.ID != prototype.Id)
                continue;

            count += TryComp<StackComponent>(part, out var stack) ? stack.Count : 1;
        }

        return count;
    }

    public FixedPoint2 GetReagentAmount(EntityUid client, ProtoId<ReagentPrototype> reagent)
    {
        if (GetLinkedSilo(client, FabricationSiloKind.Chemicals) is not { } silo ||
            !TryComp<FabricationSiloComponent>(silo, out var comp))
            return FixedPoint2.Zero;

        return comp.Reagents.GetValueOrDefault(reagent);
    }
}
