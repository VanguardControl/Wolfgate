using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Wolfmed.Reagents;

/// <summary>
/// Painkillers: what is in a body, how much pain it discounts, which states its tier may lift, and what
/// taking too much of it costs. None of it treats a wound, which is the whole point of the tiers.
/// </summary>
public sealed class WolfmedPainReliefSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly PainSystem _pain = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedWolfmedConsciousnessSystem _consciousness = default!;
    [Dependency] private readonly WolfmedDamageableSystem _damage = default!;

    private readonly List<EntityUid> _finished = new();
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedPainReliefComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<WolfmedPainReliefComponent, RejuvenateEvent>(OnRejuvenate);
    }

    /// <summary>Strongest tier in the body right now, including an open emergency window.</summary>
    public WolfmedPainReliefTier GetTier(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) ? body.Comp.Tier : WolfmedPainReliefTier.None;
    }

    /// <summary>Pain units discounted from the Downed test. Every tier counts here.</summary>
    public float GetRelief(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) ? body.Comp.Relief : 0f;
    }

    /// <summary>Pain units discounted from the Unconscious test. A weak painkiller never reaches this.</summary>
    public float GetStrongRelief(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) ? body.Comp.StrongRelief : 0f;
    }

    /// <summary>A stimulant or an emergency pen: pain decides nothing about staying on your feet.</summary>
    public bool LiftsDowned(Entity<WolfmedPainReliefComponent?> body)
    {
        return GetTier(body) >= WolfmedPainReliefTier.Stimulant;
    }

    /// <summary>Inside an emergency window: pain decides nothing at all. Blood and airloss still do.</summary>
    public bool LiftsUnconscious(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) && body.Comp.EmergencyEnds > _timing.CurTime;
    }

    /// <summary>The crash after an emergency window. Downed on its own, whatever the other inputs say.</summary>
    public bool InCrash(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) && body.Comp.CrashEnds > _timing.CurTime;
    }

    /// <summary>
    /// Strong painkillers hide the wound slowdowns, so a player can walk on a broken leg. The fracture keeps
    /// worsening underneath, which is the trade.
    /// </summary>
    public bool MasksSlowdown(EntityUid body)
    {
        return TryComp(body, out WolfmedPainReliefComponent? relief) &&
               relief.Tier is WolfmedPainReliefTier.Strong or WolfmedPainReliefTier.Emergency;
    }

    public float GetSedation(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) ? body.Comp.Sedation : 0f;
    }

    /// <summary>
    /// Adds or refreshes one reagent's dose. Doses are keyed by reagent, so two painkillers stack while one
    /// painkiller metabolising every tick does not.
    /// </summary>
    public void AddDose(EntityUid body, string key, WolfmedPainReliefTier tier, float strength,
        TimeSpan duration, float sedationPerSecond)
    {
        if (!_net.IsServer || tier == WolfmedPainReliefTier.None || duration <= TimeSpan.Zero)
            return;

        var comp = EnsureComp<WolfmedPainReliefComponent>(body);
        if (tier == WolfmedPainReliefTier.Emergency)
        {
            // One window per pen, and never during the crash the last one left behind.
            if (comp.EmergencyEnds > _timing.CurTime || comp.CrashEnds > _timing.CurTime)
                return;

            comp.EmergencyEnds = _timing.CurTime + duration;
        }

        comp.Doses[key] = new WolfmedPainReliefDose(tier, strength, sedationPerSecond,
            _timing.CurTime + duration);
        Recalculate((body, comp));
        _consciousness.Refresh(body);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        _accumulator += frameTime;
        if (_accumulator < 1f)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;

        _finished.Clear();
        var query = EntityQueryEnumerator<WolfmedPainReliefComponent>();
        while (query.MoveNext(out var uid, out var comp))
            Tick((uid, comp), elapsed);

        foreach (var uid in _finished)
            RemComp<WolfmedPainReliefComponent>(uid);
    }

    private void Tick(Entity<WolfmedPainReliefComponent> body, float elapsed)
    {
        var now = _timing.CurTime;
        var changed = false;
        foreach (var (key, dose) in body.Comp.Doses.ToArray())
        {
            if (dose.Ends > now)
                continue;

            body.Comp.Doses.Remove(key);
            changed = true;
        }

        if (body.Comp.EmergencyEnds is { } emergency && now >= emergency)
        {
            body.Comp.EmergencyEnds = null;
            body.Comp.CrashEnds = now + body.Comp.CrashDuration;
            Crash(body);
            changed = true;
        }

        if (body.Comp.CrashEnds is { } crash && now >= crash)
        {
            body.Comp.CrashEnds = null;
            changed = true;
        }

        if (UpdateSedation(body, elapsed) || changed)
            Recalculate(body);

        if (changed)
            _consciousness.Refresh(body);

        if (body.Comp.Doses.Count == 0 && body.Comp.Sedation <= 0f &&
            body.Comp.EmergencyEnds == null && body.Comp.CrashEnds == null)
            _finished.Add(body);
    }

    /// <summary>
    /// Sedation rises while a sedating painkiller is in the body and falls back when it is not. Past the
    /// threshold it depresses breathing, which is the lethal end of an overdose today.
    /// </summary>
    private bool UpdateSedation(Entity<WolfmedPainReliefComponent> body, float elapsed)
    {
        var gain = 0f;
        foreach (var dose in body.Comp.Doses.Values)
            gain += dose.SedationPerSecond;

        var old = body.Comp.Sedation;
        body.Comp.Sedation = Math.Clamp(gain > 0f
            ? body.Comp.Sedation + gain * elapsed
            : body.Comp.Sedation - body.Comp.SedationDecayPerSecond * elapsed, 0f, 1f);

        if (body.Comp.Sedation > body.Comp.SedationAirlossThreshold &&
            body.Comp.SedationAirlossThreshold < 1f)
        {
            // SEAM (BRAIN): this becomes SetExternalPressure(body, "sedation", depth) once brain
            // oxygenation exists. Until then the damage keeps an overdose lethal.
            var depth = (body.Comp.Sedation - body.Comp.SedationAirlossThreshold) /
                        (1f - body.Comp.SedationAirlossThreshold);
            _damage.ChangeDamage(body.Owner, body.Comp.SedationDamage * depth * elapsed,
                ignoreResistances: true, interruptsDoAfters: false);
        }

        if (Math.Abs(old - body.Comp.Sedation) < 0.0005f)
            return false;

        _movement.RefreshMovementSpeedModifiers(body);
        Dirty(body);
        return true;
    }

    /// <summary>The bill for an emergency pen: every part's pain goes up by a data multiplier.</summary>
    private void Crash(Entity<WolfmedPainReliefComponent> body)
    {
        if (body.Comp.CrashPainMultiplier <= 1f)
            return;

        foreach (var (part, _) in _body.GetBodyChildren(body.Owner))
        {
            if (!TryComp(part, out PainComponent? pain) || pain.Value <= FixedPoint2.Zero)
                continue;

            _pain.SetPain((part, pain), pain.Value * body.Comp.CrashPainMultiplier);
        }
    }

    private void Recalculate(Entity<WolfmedPainReliefComponent> body)
    {
        var tier = WolfmedPainReliefTier.None;
        var relief = 0f;
        var strong = 0f;
        TimeSpan? ends = null;

        foreach (var dose in body.Comp.Doses.Values)
        {
            if (dose.Tier > tier)
                tier = dose.Tier;

            relief += dose.Strength;
            if (dose.Tier >= WolfmedPainReliefTier.Strong)
                strong += dose.Strength;

            if (ends == null || dose.Ends > ends)
                ends = dose.Ends;
        }

        if (body.Comp.EmergencyEnds is { } emergency)
        {
            tier = WolfmedPainReliefTier.Emergency;
            if (ends == null || emergency > ends)
                ends = emergency;
        }

        body.Comp.Tier = tier;
        body.Comp.Relief = relief;
        body.Comp.StrongRelief = strong;
        body.Comp.Ends = ends;
        Dirty(body);
        _movement.RefreshMovementSpeedModifiers(body);
    }

    private void OnRefreshSpeed(EntityUid uid, WolfmedPainReliefComponent component,
        RefreshMovementSpeedModifiersEvent args)
    {
        if (component.Sedation <= 0f)
            return;

        args.ModifySpeed(1f - (1f - component.SedationSlowdown) * component.Sedation);
    }

    private void OnRejuvenate(EntityUid uid, WolfmedPainReliefComponent component, RejuvenateEvent args)
    {
        if (_net.IsServer)
            RemComp<WolfmedPainReliefComponent>(uid);
    }
}
