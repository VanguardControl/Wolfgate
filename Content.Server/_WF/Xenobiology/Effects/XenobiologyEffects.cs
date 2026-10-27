using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Xenobiology.Effects;

/// <summary>
/// Helpers for the xenobiology effects, which take their strength from <see cref="EntityEffectReagentArgs.Quantity"/>.
/// </summary>
public static class XenobiologyEffectArgs
{
    public static float Strength(EntityEffectBaseArgs args)
    {
        return args is EntityEffectReagentArgs reagentArgs ? reagentArgs.Quantity.Float() : 1f;
    }

    public static void SpawnNear(IEntityManager entMan, EntProtoId proto, EntityUid target, float offset)
    {
        var random = IoCManager.Resolve<IRobustRandom>();
        var xform = entMan.GetComponent<TransformComponent>(target);
        var coords = xform.Coordinates.Offset(new Vector2(random.NextFloat(-offset, offset), random.NextFloat(-offset, offset)));
        entMan.SpawnAtPosition(proto, coords);
    }
}

/// <summary>
/// Spawns <see cref="Number"/> times the strength of <see cref="Entity"/> next to the target.
/// </summary>
public sealed partial class SlimeSpawnEntity : EntityEffect
{
    [DataField(required: true)]
    public EntProtoId Entity;

    [DataField]
    public int Number = 1;

    [DataField]
    public float Offset = 0.2f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var count = (int) MathF.Round(Number * XenobiologyEffectArgs.Strength(args));
        for (var i = 0; i < count; i++)
        {
            XenobiologyEffectArgs.SpawnNear(args.EntityManager, Entity, args.TargetEntity, Offset);
        }
    }
}

/// <summary>
/// Rolls <see cref="EntityTable"/> <see cref="Number"/> times the strength and spawns the results next to the target.
/// </summary>
public sealed partial class SlimeSpawnFromTable : EntityEffect
{
    [DataField(required: true)]
    public EntityTableSelector EntityTable = default!;

    [DataField]
    public int Number = 1;

    [DataField]
    public float Offset = 0.2f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var table = args.EntityManager.System<EntityTableSystem>();
        var count = (int) MathF.Round(Number * XenobiologyEffectArgs.Strength(args));
        for (var i = 0; i < count; i++)
        {
            foreach (var proto in table.GetSpawns(EntityTable))
            {
                XenobiologyEffectArgs.SpawnNear(args.EntityManager, proto, args.TargetEntity, Offset);
            }
        }
    }
}

/// <summary>
/// Adds the strength in units of <see cref="Reagent"/> to the target's <see cref="Solution"/>.
/// </summary>
public sealed partial class SlimeAddReagent : EntityEffect
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    [DataField(required: true)]
    public string Solution = string.Empty;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var amount = FixedPoint2.New(XenobiologyEffectArgs.Strength(args));
        if (amount <= 0)
            return;

        var solutions = args.EntityManager.System<SharedSolutionContainerSystem>();
        if (solutions.TryGetSolution(args.TargetEntity, Solution, out var solution))
            solutions.TryAddReagent(solution.Value, Reagent, amount);
    }
}

/// <summary>
/// Adds <see cref="Heat"/> joules times the strength to the gas around the target.
/// </summary>
public sealed partial class SlimeAddHeat : EntityEffect
{
    [DataField(required: true)]
    public float Heat;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var atmos = args.EntityManager.System<AtmosphereSystem>();
        if (atmos.GetContainingMixture(args.TargetEntity, false, true) is { } mixture)
            atmos.AddHeat(mixture, Heat * XenobiologyEffectArgs.Strength(args));
    }
}

/// <summary>
/// Replaces the target with <see cref="Entity"/>, moving its mind across.
/// </summary>
public sealed partial class MutateIntoEntity : EntityEffect
{
    [DataField(required: true)]
    public EntProtoId Entity;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-mutate-into-entity",
            ("chance", Probability), ("entity", prototype.Index(Entity).Name));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entMan = args.EntityManager;
        if (entMan.IsQueuedForDeletion(args.TargetEntity))
            return;

        var target = args.TargetEntity;
        var newEntity = entMan.SpawnAtPosition(Entity, entMan.GetComponent<TransformComponent>(target).Coordinates);
        if (entMan.TryGetComponent<MindContainerComponent>(target, out var mind) && mind.Mind is { } mindId)
            entMan.System<SharedMindSystem>().TransferTo(mindId, newEntity);

        // Drop what the old body carried so nothing is deleted with it.
        var inventory = entMan.System<InventorySystem>();
        var slots = inventory.GetSlotEnumerator(target);
        while (slots.NextItem(out _, out var slot))
        {
            inventory.TryUnequip(target, slot.Name, true, true);
        }

        var hands = entMan.System<SharedHandsSystem>();
        foreach (var hand in hands.EnumerateHands(target))
        {
            hands.TryDrop(target, hand, checkActionBlocker: false);
        }

        entMan.QueueDeleteEntity(target);
    }
}
