using Content.Server._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared._WF.ShipCommand;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// Points a commanded ship's sentries at whoever its crew turn on: a trespasser who stayed past his warning, a thief, an
/// attacker. Every couple of seconds each sentry lets go of targets that are down or have left the ship.
/// </summary>
public sealed class WFShipSentrySystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private MobStateSystem _mobs = default!;

    private float _timer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipCommandComponent, WFCrewAttackAnsweredEvent>(OnAttackAnswered);
    }

    private void OnAttackAnswered(Entity<WFShipCommandComponent> grid, ref WFCrewAttackAnsweredEvent args)
    {
        var query = EntityQueryEnumerator<WFShipSentryComponent, TransformComponent>();
        while (query.MoveNext(out var sentry, out _, out var xform))
        {
            if (xform.GridUid == grid.Owner)
                _factions.AggroEntity(sentry, args.Attacker);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 2f)
            return;

        _timer = 0f;
        var query = EntityQueryEnumerator<WFShipSentryComponent, FactionExceptionComponent, TransformComponent>();
        while (query.MoveNext(out var sentry, out _, out var exceptions, out var xform))
        {
            foreach (var target in new List<EntityUid>(exceptions.Hostiles))
            {
                if (TerminatingOrDeleted(target) || !_mobs.IsAlive(target) || Transform(target).GridUid != xform.GridUid)
                    _factions.DeAggroEntity(sentry, target);
            }
        }
    }
}
