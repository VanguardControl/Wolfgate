using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>What a defibrillator and a pair of hands on a chest are worth on a wound host.</summary>
/// <remarks>
/// Nothing here ever marks a body unrevivable. A failed shock is a shock that can be tried again; rot is
/// the only hard stop and it is not this system's.
/// </remarks>
public sealed class WolfmedRevivalSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;

    /// <summary>Test seam, mirroring <c>WolfmedEviscerationSystem.ForcedRoll</c>: a forced chance roll.</summary>
    public float? ForcedRoll;

    /// <summary>True when the defibrillator and CPR should ask this system instead of the damage thresholds.</summary>
    public bool OwnsRevival(EntityUid body) => _life.OwnsDeath(body);

    /// <summary>
    /// One shock. Returns whether the heart restarted, with the line the paddles should say either way.
    /// The caller has already refused a rotten body and anything carrying an <c>UnrevivableComponent</c>.
    /// </summary>
    public bool TryDefibrillate(EntityUid body, out string message)
    {
        message = NoResponse;

        if (GetRefusal(body) is { } refusal)
        {
            message = refusal;
            return false;
        }

        if (!Roll(GetChance(body)))
            return false;

        Revive(body);
        message = "wolfmed-defib-success";
        return true;
    }

    /// <summary>The line a failed roll gets, as opposed to a gate that no number of shocks will move.</summary>
    public const string NoResponse = "wolfmed-defib-no-response";

    /// <summary>
    /// Why the paddles will not even charge, or null when they will. Separate from the shock itself so a
    /// machine can say what is wrong instead of zapping a body it was never going to restart.
    /// </summary>
    public string? GetRefusal(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !OwnsRevival(body))
            return NoResponse;

        if (!_life.HasBrain(body))
            return "wolfmed-defib-no-brain";

        if (_life.GetBlood(body) <= _cfg.GetCVar(WolfmedCVars.DefibBlood))
            return "wolfmed-defib-no-blood";

        // A destroyed brain has to be put back together first; see SurgeryRepairBrain.
        if (_life.GetBrainOrgan(body) is not { } organ || organ.Comp.Health <= FixedPoint2.Zero)
            return "wolfmed-defib-brain-dead";

        return null;
    }

    /// <summary>
    /// The paddles' odds. On a body still on the arrest clock they are scaled by how much oxygen is left in
    /// the brain, which is what makes speed matter. A corpse has no circulation and therefore no way to
    /// raise that number, so once its brain has been repaired and its blood put back it gets the flat base
    /// chance: the surgery was the work, and a medic who has done it should not be told "no response"
    /// eight times in a row for a reason nothing on the body shows.
    /// </summary>
    public float GetChance(EntityUid body)
    {
        if (_life.GetBrainOrgan(body) is not { } organ || organ.Comp.Health <= FixedPoint2.Zero)
            return 0f;

        var chance = _cfg.GetCVar(WolfmedCVars.DefibChance);
        if (_mobState.IsDead(body))
            return Math.Clamp(chance, 0f, 1f);

        var floor = Math.Clamp(_cfg.GetCVar(WolfmedCVars.DefibOxygenationFloor), 0f, 1f);
        var oxygen = Math.Clamp(_life.GetOxygenation(body), 0f, 1f);
        return Math.Clamp(chance * (floor + (1f - floor) * oxygen), 0f, 1f);
    }

    /// <summary>Heart going again, brain with something in it, and consciousness deciding the rest.</summary>
    public void Revive(EntityUid body)
    {
        var wasDead = _mobState.IsDead(body);
        _life.EndArrest(body);

        // A body that had already died comes back on what the paddles put into it and nothing else, so a
        // corpse is always unconscious afterwards however the brain got repaired.
        _life.SetOxygenation(body, wasDead
            ? WolfmedLifeSystem.RestoredOxygenation
            : MathF.Max(_life.GetOxygenation(body), WolfmedLifeSystem.RestoredOxygenation));

        if (wasDead && _mobState.HasState(body, MobState.Critical))
            _mobState.ChangeMobState(body, MobState.Critical);

        // One zero-length tick so the hypoxia pressure is rewritten before consciousness reads it: a body
        // brought back on 35% oxygenation is unconscious, not up and walking.
        _life.Tick(body, 0.0001f);
        _consciousness.Refresh(body);
    }

    /// <summary>
    /// CPR: hands on the chest for the duration of the do-after. It buys time on the oxygenation clock and
    /// moves a little blood; it never restarts the heart and it never revives anybody on its own.
    /// </summary>
    public bool StartCpr(EntityUid body, TimeSpan duration)
    {
        if (TerminatingOrDeleted(body) || !OwnsRevival(body))
            return false;

        var cpr = EnsureComp<WolfmedCprComponent>(body);
        cpr.Ends = _timing.CurTime + duration;
        return true;
    }

    private bool Roll(float chance)
    {
        var clamped = Math.Clamp(chance, 0f, 1f);
        return ForcedRoll is { } forced ? forced < clamped : _random.Prob(clamped);
    }
}
