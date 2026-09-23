using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// The Wolfmed half of Onyx's <see cref="PainSystem"/> (M1a, plan §3.1). One pain number: a body's pain is
/// min(soft cap, Σ parts), suppression is shared out once across the parts, and the pain shock reads its
/// lines from CVars. Adrenaline no longer lowers pain (OD5): it speeds the crawl and lifts the Downed
/// do-after penalty instead.
/// </summary>
public sealed class WolfmedBodyPainSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;

    public FixedPoint2 ShockThreshold => FixedPoint2.New(_cfg.GetCVar(WolfmedCVars.PainShockThreshold));

    public FixedPoint2 ShockRearm => FixedPoint2.New(_cfg.GetCVar(WolfmedCVars.PainShockRearm));

    public TimeSpan AdrenalineTime => TimeSpan.FromSeconds(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.AdrenalineSeconds)));

    public float AdrenalineCrawlMultiplier => MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.AdrenalineCrawlMultiplier));

    /// <summary>
    /// P13: the pain a body with feeling parts carries is the sum of the parts, capped at its soft cap,
    /// whatever anything tries to set it to. False for a part, or a body with no part that feels pain.
    /// </summary>
    public bool TryGetDerivedPain(EntityUid uid, out FixedPoint2 value)
    {
        value = FixedPoint2.Zero;
        if (HasComp<BodyPartComponent>(uid))
            return false;

        var any = false;
        foreach (var (part, _) in _body.GetBodyChildren(uid))
        {
            if (!TryComp(part, out PainComponent? pain))
                continue;

            any = true;
            value += pain.Value;
        }

        return any;
    }

    /// <summary>
    /// The share of the body's suppression one part carries: its pain over the sum of the parts, so the
    /// shares add up to 1 and the body's suppression is taken off once, not once per part.
    /// </summary>
    public float SuppressionShare(Entity<PainComponent> part, EntityUid body)
    {
        var sum = 0f;
        foreach (var (child, _) in _body.GetBodyChildren(body))
        {
            if (TryComp(child, out PainComponent? pain))
                sum += pain.Value.Float();
        }

        return sum > 0f ? part.Comp.Value.Float() / sum : 0f;
    }

    /// <summary>Adrenaline from a pain shock is running on this body.</summary>
    public bool HasAdrenaline(EntityUid body) =>
        TryComp(body, out PainShockTargetComponent? shock) && shock.AdrenalineEnds > _timing.CurTime;

    /// <summary>Called by the pain shock when its adrenaline starts or runs out.</summary>
    public void AdrenalineChanged(EntityUid body, bool started)
    {
        _movement.RefreshMovementSpeedModifiers(body);
        var ev = new WolfmedAdrenalineEvent(body, started);
        RaiseLocalEvent(ref ev);
    }
}

/// <summary>Broadcast when a pain shock's adrenaline starts or runs out. The patient is told both.</summary>
[ByRefEvent]
public readonly record struct WolfmedAdrenalineEvent(EntityUid Body, bool Started);
