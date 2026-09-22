using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Consciousness;

/// <summary>
/// What kills a wound host now that damage totals do not: no brain in the body, no blood left, or no air for
/// long enough. A stand-in until the BRAIN package brings cardiac arrest and the oxygenation clock.
/// </summary>
public sealed class WolfmedLifeSystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextPoll;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedPartAmputatedEvent>(OnAmputated, after: [typeof(Wounds.WolfmedDismembermentSystem)]);
    }

    private void OnAmputated(ref WolfmedPartAmputatedEvent args)
    {
        // A head coming off takes the brain with it.
        if (TryComp(args.Body, out WolfmedConsciousnessComponent? consciousness))
            Check((args.Body, consciousness));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextPoll)
            return;

        _nextPoll = _timing.CurTime + PollInterval;
        var query = EntityQueryEnumerator<WolfmedConsciousnessComponent>();
        while (query.MoveNext(out var uid, out var comp))
            Check((uid, comp));
    }

    /// <summary>Kills the body if any lethal condition holds. Returns the reason, or null.</summary>
    public string? Check(Entity<WolfmedConsciousnessComponent> body)
    {
        if (TerminatingOrDeleted(body) || _mobState.IsDead(body) || !_mobState.HasState(body, MobState.Dead))
            return null;

        var reason = Reason(body);
        if (reason == null)
            return null;

        _mobState.ChangeMobState(body, MobState.Dead);
        return reason;
    }

    private string? Reason(EntityUid body)
    {
        if (TryComp(body, out BodyComponent? bodyComp) &&
            !_body.TryGetBodyOrganEntityComps<BrainComponent>((body, bodyComp), out _))
            return "no brain";

        if (HasComp<BloodstreamComponent>(body) &&
            _bloodstream.GetBloodLevelPercentage(body) <= _cfg.GetCVar(WolfmedCVars.LifeBloodDead))
            return "no blood";

        if (TryComp(body, out DamageableComponent? damageable) &&
            damageable.DamagePerGroup.TryGetValue("Airloss", out var airloss) &&
            airloss >= FixedPoint2.New(_cfg.GetCVar(WolfmedCVars.LifeAirlossDead)))
            return "no air";

        return null;
    }
}
