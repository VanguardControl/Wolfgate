using System.Linq;
using System.Numerics;
using Content.Shared._WF.Xenobiology;
using Content.Shared.Damage;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenobiology;

/// <summary>
/// Pulls dead slimes into collecting processors and turns them into extracts when processing ends.
/// </summary>
public sealed partial class SlimeProcessorUpdateSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SlimeProcessorSystem _processor = default!;

    private readonly HashSet<Entity<SlimeComponent>> _nearby = new();
    private readonly List<Entity<SlimeProcessorComponent>> _finished = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        _finished.Clear();
        var active = EntityQueryEnumerator<ActiveSlimeProcessorComponent, SlimeProcessorComponent>();
        while (active.MoveNext(out var uid, out var processing, out var processor))
        {
            if (processing.ProcessingFinishedMoment <= now)
                _finished.Add((uid, processor));
        }

        foreach (var processor in _finished)
        {
            Process(processor);
            _processor.StartCollecting(processor);
        }

        var collecting = EntityQueryEnumerator<CollectingSlimeProcessorComponent, SlimeProcessorComponent>();
        while (collecting.MoveNext(out var uid, out var collect, out var processor))
        {
            if (collect.SlimeAcquireMoment > now)
                continue;

            collect.SlimeAcquireMoment = now + processor.SlimeAcquireCooldown;
            TryCollect((uid, processor));
        }
    }

    private void Process(Entity<SlimeProcessorComponent> ent)
    {
        if (!_container.TryGetContainer(ent, SlimeProcessorComponent.SlimeContainerName, out var slimes))
            return;

        var coords = Transform(ent).Coordinates;
        foreach (var slime in slimes.ContainedEntities.ToArray())
        {
            if (TryComp<SlimeComponent>(slime, out var slimeComp))
            {
                var count = ent.Comp.YieldMultiplier + slimeComp.SlimeSteroidAmount;
                for (var i = 0; i < count; i++)
                {
                    var offset = new Vector2(_random.NextFloat(-0.2f, 0.2f), _random.NextFloat(-0.2f, 0.2f));
                    SpawnAtPosition(slimeComp.Extract, coords.Offset(offset));
                }
            }

            QueueDel(slime);
        }
    }

    private void TryCollect(Entity<SlimeProcessorComponent> ent)
    {
        if (!_container.TryGetContainer(ent, SlimeProcessorComponent.SlimeContainerName, out var slimes))
            return;

        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.CollectRange, _nearby);
        foreach (var slime in _nearby)
        {
            if (_container.IsEntityOrParentInContainer(slime)
                || !TryComp<DamageableComponent>(slime, out var damage)
                || damage.TotalDamage < ent.Comp.CollectDamageThreshold)
                continue;

            _container.Insert(slime.Owner, slimes);
            return;
        }
    }
}
