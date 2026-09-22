using Content.Server.Body.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Consciousness;

/// <summary>
/// Owns Alive and Critical on a wound host. Damage totals decide nothing here: pain, blood, working legs and
/// outside pressures are each read against their own thresholds, the worst one wins, and hysteresis keeps a
/// bleeding body from flickering between states.
/// </summary>
/// <remarks>
/// Dead is not this system's to give. It stays on the paths that never went through the damage thresholds (a
/// destroyed brain, a gib, an admin) until the BRAIN package lands the second meter.
/// </remarks>
public sealed class WolfmedConsciousnessSystem : SharedWolfmedConsciousnessSystem
{
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PainSystem _pain = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly WolfmedPainReliefSystem _relief = default!;

    /// <summary>
    /// How much of an external pressure is enough to put a body on the floor. 1 is unconscious, so anything
    /// most of the way there is Downed first.
    /// </summary>
    private const float PressureDownShare = 0.7f;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(0.5);

    private float _painDown;
    private float _painOut;
    private float _bloodDown;
    private float _bloodOut;
    private float _hysteresis;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_configuration, WolfmedCVars.ConsciousnessPainDown, value => _painDown = value, true);
        Subs.CVar(_configuration, WolfmedCVars.ConsciousnessPainOut, value => _painOut = value, true);
        Subs.CVar(_configuration, WolfmedCVars.ConsciousnessBloodDown, value => _bloodDown = value, true);
        Subs.CVar(_configuration, WolfmedCVars.ConsciousnessBloodOut, value => _bloodOut = value, true);
        Subs.CVar(_configuration, WolfmedCVars.ConsciousnessHysteresis, value => _hysteresis = value, true);

        SubscribeLocalEvent<WoundHostComponent, ComponentStartup>(OnWoundHostStartup);
        SubscribeLocalEvent<WolfmedConsciousnessComponent, PainChangedEvent>(OnPainChanged);
        SubscribeLocalEvent<WolfmedConsciousnessComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WolfmedConsciousnessComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WolfmedConsciousnessComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<BodyPartFunctionalityComponent, BodyPartFunctionalityChangedEvent>(OnFunctionality);
    }

    private void OnWoundHostStartup(Entity<WoundHostComponent> ent, ref ComponentStartup args)
    {
        EnsureComp<WolfmedConsciousnessComponent>(ent);
    }

    /// <inheritdoc/>
    public override void SetExternalPressure(EntityUid body, string key, float level)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? consciousness))
            return;

        if (level <= 0f)
        {
            if (!consciousness.Pressures.Remove(key))
                return;
        }
        else
        {
            if (consciousness.Pressures.TryGetValue(key, out var old) && MathF.Abs(old - level) < 0.001f)
                return;

            consciousness.Pressures[key] = level;
        }

        Evaluate((body, consciousness));
    }

    /// <inheritdoc/>
    public override void Refresh(EntityUid body)
    {
        if (TryComp(body, out WolfmedConsciousnessComponent? consciousness))
            Evaluate((body, consciousness));
    }

    /// <summary>
    /// The bloodstream raises nothing when its volume changes, so it hands the level over on the tick where
    /// it already knows it. One marked call in <c>BloodstreamSystem.Update</c>.
    /// </summary>
    public void OnBloodLevelChanged(EntityUid body, float fraction)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? consciousness) ||
            MathF.Abs(consciousness.BloodFraction - fraction) < 0.001f)
            return;

        consciousness.BloodFraction = fraction;
        Evaluate((body, consciousness));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Only bodies with something still moving are polled; a healthy crew costs nothing.
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WolfmedConsciousnessComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Watching || now < comp.NextEvaluation)
                continue;

            comp.NextEvaluation = now + PollInterval;
            Evaluate((uid, comp));
        }
    }

    /// <summary>Reads every input, picks the worst, and applies the state it names.</summary>
    public void Evaluate(Entity<WolfmedConsciousnessComponent> body)
    {
        if (TerminatingOrDeleted(body) || !OwnsMobState(body))
            return;

        if (_mobState.IsDead(body))
        {
            Apply(body, WolfmedConsciousness.Unconscious, 1f, false);
            return;
        }

        var (painDown, painOut) = GetPainLevels(body);
        var (bloodDown, bloodOut) = GetBloodLevels(body);
        var pressure = GetPressure(body);

        var downLevel = MathF.Max(painDown, MathF.Max(bloodDown, pressure / PressureDownShare));
        var outLevel = MathF.Max(painOut, MathF.Max(bloodOut, pressure));

        if (LegsGone(body))
            downLevel = MathF.Max(downLevel, 1f);

        if (_relief.InCrash(body.Owner))
            downLevel = MathF.Max(downLevel, 1f);

        body.Comp.DownLevel = downLevel;
        body.Comp.OutLevel = outLevel;

        // Hysteresis: entering a state takes 1, leaving it takes falling back below 1 - the CVar.
        var leaving = 1f - _hysteresis;
        var target = body.Comp.State switch
        {
            WolfmedConsciousness.Unconscious when outLevel >= leaving => WolfmedConsciousness.Unconscious,
            WolfmedConsciousness.Unconscious when downLevel >= leaving => WolfmedConsciousness.Downed,
            WolfmedConsciousness.Downed when outLevel >= 1f => WolfmedConsciousness.Unconscious,
            WolfmedConsciousness.Downed when downLevel >= leaving => WolfmedConsciousness.Downed,
            WolfmedConsciousness.Downed => WolfmedConsciousness.Up,
            _ when outLevel >= 1f => WolfmedConsciousness.Unconscious,
            _ when downLevel >= 1f => WolfmedConsciousness.Downed,
            _ => WolfmedConsciousness.Up,
        };

        var watching = downLevel > 0f || outLevel > 0f || target != WolfmedConsciousness.Up ||
                       _relief.GetTier(body.Owner) != WolfmedPainReliefTier.None;

        Apply(body, target, Depth(target, downLevel, outLevel, bloodOut, pressure), watching);
    }

    /// <summary>Downed carries the first third of the dying view, Unconscious the rest.</summary>
    private static float Depth(WolfmedConsciousness state, float downLevel, float outLevel, float bloodOut,
        float pressure)
    {
        return state switch
        {
            WolfmedConsciousness.Downed => 0.35f + 0.2f * Math.Clamp(outLevel, 0f, 1f),
            WolfmedConsciousness.Unconscious => 0.55f + 0.45f * Math.Clamp(
                MathF.Max(bloodOut - 1f, pressure - 1f), 0f, 1f),
            _ => 0.35f * Math.Clamp(downLevel, 0f, 1f),
        };
    }

    private void Apply(Entity<WolfmedConsciousnessComponent> body, WolfmedConsciousness state, float depth,
        bool watching)
    {
        body.Comp.Watching = watching;

        if (body.Comp.State != state || MathF.Abs(body.Comp.Depth - depth) >= 0.005f)
        {
            body.Comp.State = state;
            body.Comp.Depth = depth;
            Dirty(body);
        }

        if (state == WolfmedConsciousness.Downed)
            EnsureComp<WolfmedDownedComponent>(body);
        else
            RemComp<WolfmedDownedComponent>(body);

        if (_mobState.IsDead(body))
            return;

        var mobState = state == WolfmedConsciousness.Unconscious ? MobState.Critical : MobState.Alive;
        if (_mobState.HasState(body, mobState))
            _mobState.ChangeMobState(body, mobState);
    }

    /// <summary>
    /// Downed reads the effective pain the vignette reads; Unconscious reads the pain before the soft clamp,
    /// which is the sum of the parts (the body's own value is clamped, so it can never say more than 1.0).
    /// </summary>
    private (float Down, float Out) GetPainLevels(Entity<WolfmedConsciousnessComponent> body)
    {
        if (!TryComp(body, out PainComponent? pain) || pain.SoftPainCap <= FixedPoint2.Zero)
            return (0f, 0f);

        var cap = pain.SoftPainCap.Float();
        var down = 0f;
        var outLevel = 0f;

        if (!_relief.LiftsDowned(body.Owner) && _painDown > 0f)
        {
            var effective = _pain.GetPain((body.Owner, pain)).Float() - _relief.GetRelief(body.Owner);
            down = MathF.Max(0f, effective) / (cap * _painDown);
        }

        if (!_relief.LiftsUnconscious(body.Owner) && _painOut > 0f)
        {
            var uncapped = GetUncappedPain(body) - _relief.GetStrongRelief(body.Owner);
            outLevel = MathF.Max(0f, uncapped) / (cap * _painOut);
        }

        return (down, outLevel);
    }

    /// <summary>
    /// Pain before the soft clamp. <c>PainSystem.SetPain</c> clamps the part and the body alike to the soft
    /// cap, so the body's own value tops out at exactly 1.0 of it; the parts still carry the rest.
    /// </summary>
    public float GetUncappedPain(EntityUid body)
    {
        var total = 0f;
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (TryComp(part, out PainComponent? pain))
                total += _pain.GetPain((part, pain)).Float();
        }

        return total;
    }

    /// <summary>Blood volume, as how far past each threshold the body is. No bloodstream, no input.</summary>
    private (float Down, float Out) GetBloodLevels(Entity<WolfmedConsciousnessComponent> body)
    {
        if (!HasComp<BloodstreamComponent>(body))
            return (0f, 0f);

        var fraction = Math.Clamp(body.Comp.BloodFraction, 0f, 1f);
        var down = _bloodDown < 1f ? (1f - fraction) / (1f - _bloodDown) : 0f;
        var outLevel = _bloodOut < 1f ? (1f - fraction) / (1f - _bloodOut) : 0f;
        return (MathF.Max(0f, down), MathF.Max(0f, outLevel));
    }

    /// <summary>The worst thing pushed in from outside, airloss included.</summary>
    private static float GetPressure(Entity<WolfmedConsciousnessComponent> body)
    {
        var worst = 0f;
        foreach (var level in body.Comp.Pressures.Values)
            worst = MathF.Max(worst, level);

        return worst;
    }

    /// <summary>
    /// Both legs gone or disabled. Shitmed already drops the body when the last leg leaves; this reads the
    /// same state as a consciousness input, so the two agree instead of fighting.
    /// </summary>
    private bool LegsGone(EntityUid body)
    {
        if (!TryComp(body, out BodyComponent? comp) || comp.RequiredLegs <= 0)
            return false;

        foreach (var (part, bodyPart) in _body.GetBodyChildren(body, comp))
        {
            if (bodyPart.PartType != BodyPartType.Leg)
                continue;

            if (CompOrNull<BodyPartFunctionalityComponent>(part)?.State !=
                BodyPartFunctionalityState.Disabled)
                return false;
        }

        return true;
    }

    private void OnPainChanged(Entity<WolfmedConsciousnessComponent> body, ref PainChangedEvent args)
    {
        Evaluate(body);
    }

    /// <summary>
    /// Damage still moves pain and blood indirectly, so a re-evaluation is worth the event. Suffocation is
    /// no longer read here at all: BRAIN's oxygenation clock owns it and pushes its own pressure in.
    /// </summary>
    private void OnDamageChanged(Entity<WolfmedConsciousnessComponent> body, ref DamageChangedEvent args)
    {
        Evaluate(body);
    }

    private void OnMobStateChanged(EntityUid uid, WolfmedConsciousnessComponent comp,
        MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        // Death is someone else's (a destroyed brain, a gib, an admin). Drop the Downed restrictions.
        RemComp<WolfmedDownedComponent>(uid);
        comp.State = WolfmedConsciousness.Unconscious;
        comp.Depth = 1f;
        comp.Watching = false;
        Dirty(uid, comp);
    }

    private void OnRejuvenate(EntityUid uid, WolfmedConsciousnessComponent comp, RejuvenateEvent args)
    {
        comp.Pressures.Clear();
        comp.BloodFraction = 1f;
        comp.Oxygenation = 1f;
        comp.DownLevel = 0f;
        comp.OutLevel = 0f;
        comp.State = WolfmedConsciousness.Up;
        comp.Depth = 0f;
        Dirty(uid, comp);
        RemComp<WolfmedDownedComponent>(uid);

        // The damage thresholds no longer revive a wound host, so this is the only thing that does. The
        // re-evaluation waits for the next tick, by which time every other rejuvenate handler has run.
        if (_mobState.HasState(uid, MobState.Alive))
            _mobState.ChangeMobState(uid, MobState.Alive);

        comp.Watching = true;
        comp.NextEvaluation = TimeSpan.Zero;
    }

    private void OnFunctionality(Entity<BodyPartFunctionalityComponent> part,
        ref BodyPartFunctionalityChangedEvent args)
    {
        Refresh(args.Body);
    }
}
