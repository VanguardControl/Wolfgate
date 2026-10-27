using Content.Shared._WF.Xenobiology;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenobiology;

/// <summary>
/// Reactions waiting on an extract's delay, keyed by reaction with the time they fire.
/// </summary>
[RegisterComponent]
public sealed partial class SlimeExtractActiveReactionComponent : Component
{
    [ViewVariables]
    public Dictionary<ProtoId<ExtractReactionPrototype>, TimeSpan> ActiveReactions = new();
}

/// <summary>
/// Runs slime extract reactions when the extract's solution meets their requirements.
/// </summary>
public sealed partial class SlimeExtractReactionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    private readonly List<ProtoId<ExtractReactionPrototype>> _due = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeExtractComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<SlimeExtractActiveReactionComponent, EntityUnpausedEvent>(OnUnpaused);
    }

    private void OnSolutionChanged(Entity<SlimeExtractComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId != ent.Comp.ContainerName || ent.Comp.RemainingUses <= 0 || TerminatingOrDeleted(ent))
            return;

        var active = EnsureComp<SlimeExtractActiveReactionComponent>(ent);
        foreach (var id in ent.Comp.ExtractReactions)
        {
            var reaction = _proto.Index(id);
            if (!MeetsRequirements(reaction.Requirements, args.Solution))
                active.ActiveReactions.Remove(id);
            else if (!active.ActiveReactions.ContainsKey(id))
                active.ActiveReactions[id] = _timing.CurTime + reaction.Delay;
        }

        if (active.ActiveReactions.Count == 0)
            RemCompDeferred<SlimeExtractActiveReactionComponent>(ent);
    }

    private void OnUnpaused(Entity<SlimeExtractActiveReactionComponent> ent, ref EntityUnpausedEvent args)
    {
        foreach (var id in ent.Comp.ActiveReactions.Keys)
        {
            ent.Comp.ActiveReactions[id] += args.PausedTime;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SlimeExtractComponent, SlimeExtractActiveReactionComponent>();
        while (query.MoveNext(out var uid, out var extract, out var active))
        {
            if (extract.RemainingUses <= 0)
            {
                RemCompDeferred<SlimeExtractActiveReactionComponent>(uid);
                continue;
            }

            _due.Clear();
            foreach (var (id, fireAt) in active.ActiveReactions)
            {
                if (fireAt <= now)
                    _due.Add(id);
            }

            if (_due.Count == 0 || !_solutions.TryGetSolution(uid, extract.ContainerName, out var solutionEnt, out var solution))
                continue;

            // Running a reaction changes the solution, which re-checks and may re-queue the reactions.
            var fired = false;
            var delete = false;
            foreach (var id in _due)
            {
                active.ActiveReactions.Remove(id);
                var reaction = _proto.Index(id);
                if (!MeetsRequirements(reaction.Requirements, solution))
                    continue;

                RunReaction(uid, reaction, solutionEnt.Value, solution);
                fired = true;
                delete |= reaction.ShouldDelete;
            }

            if (!fired)
                continue;

            extract.RemainingUses--;
            Dirty(uid, extract);

            if (extract.RemainingUses <= 0)
            {
                if (delete)
                    QueueDel(uid);
                else
                    RemCompDeferred<SlimeExtractActiveReactionComponent>(uid);
            }
        }
    }

    private void RunReaction(EntityUid uid, ExtractReactionPrototype reaction, Entity<SolutionComponent> solutionEnt, Solution solution)
    {
        var ratio = MinimumRatio(reaction.Requirements, solution);
        foreach (var (reagent, amount) in reaction.Requirements)
        {
            solution.RemoveReagent(new ReagentQuantity(new ReagentId(reagent, null), ratio * amount), false, true);
        }

        _solutions.UpdateChemicals(solutionEnt, needsReactionsProcessing: false);

        foreach (var scaled in reaction.Effects)
        {
            var strength = ratio * scaled.ScalingFactor + scaled.ScalingOffset;
            var args = new EntityEffectReagentArgs(uid, EntityManager, null, solution, strength, null, null, 1);
            if (scaled.Effect.ShouldApply(args))
                scaled.Effect.Effect(args);
        }
    }

    private static bool MeetsRequirements(Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> requirements, Solution solution)
    {
        foreach (var (reagent, amount) in requirements)
        {
            if (solution.GetTotalPrototypeQuantity(reagent) < amount)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The smallest of supplied / required over all requirements.
    /// </summary>
    public static FixedPoint2 MinimumRatio(Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> requirements, Solution solution)
    {
        var ratio = FixedPoint2.MaxValue;
        foreach (var (reagent, amount) in requirements)
        {
            ratio = FixedPoint2.Min(ratio, solution.GetTotalPrototypeQuantity(reagent) / amount);
        }

        return ratio;
    }
}
