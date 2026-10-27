using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.Preconditions;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared._WF.Xenobiology;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Xenobiology.NPC;

/// <summary>
/// Bites the target in <see cref="TargetKey"/>.
/// </summary>
public sealed partial class SlimeEatOperator : HTNOperator
{
    [Dependency] private IEntityManager _entMan = default!;

    private SlimeSystem _slime = default!;
    private SlimeBrainSystem _brain = default!;

    [DataField(required: true)]
    public string TargetKey = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _slime = sysManager.GetEntitySystem<SlimeSystem>();
        _brain = sysManager.GetEntitySystem<SlimeBrainSystem>();
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        blackboard.Remove<EntityUid>(TargetKey);
        base.TaskShutdown(blackboard, status);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent<SlimeComponent>(owner, out var slime)
            || !blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entMan)
            || _entMan.Deleted(target)
            || !_brain.IsEdible(target)
            || !_slime.TryEat((owner, slime), target))
            return HTNOperatorStatus.Failed;

        return HTNOperatorStatus.Finished;
    }
}

/// <summary>
/// Adds nearby entities with <see cref="TargetFoodTag"/> to the slimes' known food.
/// </summary>
public sealed partial class SlimeFindEdibleTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entMan = default!;

    private SlimeBrainSystem _brain = default!;
    private EntityLookupSystem _lookup = default!;
    private TagSystem _tag = default!;

    [DataField(required: true)]
    public ProtoId<TagPrototype> TargetFoodTag;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _brain = sysManager.GetEntitySystem<SlimeBrainSystem>();
        _lookup = sysManager.GetEntitySystem<EntityLookupSystem>();
        _tag = sysManager.GetEntitySystem<TagSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.HasComponent<SlimeComponent>(owner))
            return (false, null);

        foreach (var entity in _lookup.GetEntitiesInRange(owner, SlimeBrainSystem.FoodSearchRange))
        {
            if (_tag.HasTag(entity, TargetFoodTag) && _brain.TryAddTargetFood(entity))
                return (true, null);
        }

        return (false, null);
    }
}

/// <summary>
/// Picks known food in range that the slime can path to.
/// </summary>
public sealed partial class SlimeTargetKnownEdibleTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entMan = default!;

    private SlimeBrainSystem _brain = default!;
    private EntityLookupSystem _lookup = default!;
    private PathfindingSystem _pathfinding = default!;

    [DataField(required: true)]
    public string TargetKey = string.Empty;

    [DataField(required: true)]
    public string TargetMoveKey = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _brain = sysManager.GetEntitySystem<SlimeBrainSystem>();
        _lookup = sysManager.GetEntitySystem<EntityLookupSystem>();
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.HasComponent<SlimeComponent>(owner))
            return (false, null);

        foreach (var entity in _lookup.GetEntitiesInRange(owner, SlimeBrainSystem.FoodSearchRange))
        {
            if (!_brain.IsKnownFood(entity))
                continue;

            var path = await _pathfinding.GetPath(owner, entity, SharedInteractionSystem.InteractionRange - 1f, cancelToken);
            if (path.Result == PathResult.NoPath)
                continue;

            return (true, new Dictionary<string, object>
            {
                { TargetKey, entity },
                { TargetMoveKey, _entMan.GetComponent<TransformComponent>(entity).Coordinates },
                { NPCBlackboard.PathfindKey, path },
            });
        }

        return (false, null);
    }
}

/// <summary>
/// Queues a split into <see cref="SplitAmount"/> slimes.
/// </summary>
public sealed partial class SlimeSplitOperator : HTNOperator
{
    [Dependency] private IEntityManager _entMan = default!;

    private SlimeSystem _slime = default!;

    [DataField(required: true)]
    public int SplitAmount;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _slime = sysManager.GetEntitySystem<SlimeSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.HasComponent<SlimeComponent>(owner))
            return HTNOperatorStatus.Failed;

        _slime.QueueSplit(owner, SplitAmount);
        return HTNOperatorStatus.Finished;
    }
}

/// <summary>
/// Met when the owner is fuller than <see cref="MinHungerState"/>.
/// </summary>
public sealed partial class SlimeNotHungryPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entMan = default!;

    [DataField(required: true)]
    public HungerThreshold MinHungerState = HungerThreshold.Okay;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entMan)
            && _entMan.TryGetComponent<HungerComponent>(owner, out var hunger)
            && hunger.CurrentThreshold > MinHungerState;
    }
}
