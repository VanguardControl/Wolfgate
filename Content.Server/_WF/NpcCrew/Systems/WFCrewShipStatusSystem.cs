using Content.Server._Mono.FireControl;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Decides when a ship is out of the fight, and by each crewed ship's own rule when its crew stop attacking it.
/// </summary>
public sealed partial class WFCrewShipStatusSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How long a condition must hold without a break before it counts.</summary>
    public static readonly TimeSpan DisabledDelay = TimeSpan.FromSeconds(10);

    /// <summary>A ship with less than this share of the most thrust it was seen with cannot manoeuvre.</summary>
    private const float CrippledThrust = 0.25f;

    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(2);
    // A gap this long between readings means the spell was not watched continuously.
    private static readonly TimeSpan ReadingGap = TimeSpan.FromSeconds(5);

    private readonly Dictionary<EntityUid, (TimeSpan Until, Reading Reading)> _cache = new();
    private readonly Dictionary<EntityUid, float> _peakThrust = new();
    private readonly HashSet<EntityUid> _armed = new();
    private readonly HashSet<EntityUid> _crewed = new();
    private readonly Dictionary<(EntityUid Grid, Condition Condition), (TimeSpan Since, TimeSpan Last)> _spells = new();
    private readonly Dictionary<EntityUid, (WFCrewDisengage Rule, float Range)> _policies = new();

    private enum Condition : byte { Abandoned, Disarmed, Crippled }

    private readonly record struct Reading(bool Abandoned, bool Disarmed, bool Crippled);

    /// <summary>Sets when the crew of a ship stop attacking other ships.</summary>
    public void SetPolicy(EntityUid grid, WFCrewDisengage rule, float range)
    {
        _policies[grid] = (rule, range);
    }

    /// <summary>
    /// Whether the crew of <paramref name="grid"/> should leave <paramref name="target"/> alone under their rule.
    /// <paramref name="range"/> lets a deterring crew also give up on a ship that is far enough away; an Attack task
    /// passes false, because it starts out of range.
    /// </summary>
    public bool ShouldDisengage(EntityUid grid, EntityUid target, bool range = true)
    {
        var (rule, distance) = _policies.TryGetValue(grid, out var policy) ? policy : (WFCrewDisengage.Disable, 0f);
        var reading = Read(target);
        if (reading.Abandoned)
            return true;
        if (rule == WFCrewDisengage.Destroy)
            return false;
        if (reading.Disarmed || reading.Crippled)
            return true;
        if (rule != WFCrewDisengage.Deter || !range || TerminatingOrDeleted(grid) || TerminatingOrDeleted(target))
            return false;

        var here = _transform.GetMapCoordinates(grid);
        var there = _transform.GetMapCoordinates(target);
        return here.MapId != there.MapId || (here.Position - there.Position).LengthSquared() > distance * distance;
    }

    /// <summary>
    /// Whether a ship is out of the fight: for <see cref="DisabledDelay"/> without a break, everyone who was aboard
    /// is dead or gone, or it had ship weapons and none is powered, or it has no powered weapon and under a quarter
    /// of its thrust. Each is judged against what the ship was seen with earlier, so stations, wrecks, asteroids and
    /// drones are never disabled for lacking what they never had.
    /// </summary>
    public bool IsDisabled(EntityUid grid)
    {
        var reading = Read(grid);
        return reading.Abandoned || reading.Disarmed || reading.Crippled;
    }

    private Reading Read(EntityUid grid)
    {
        var now = _timing.CurTime;
        if (_cache.TryGetValue(grid, out var cached) && now < cached.Until)
            return cached.Reading;

        var reading = default(Reading);
        if (!TerminatingOrDeleted(grid) && TryComp<ShuttleComponent>(grid, out var shuttle))
        {
            var thrust = 0f;
            foreach (var direction in shuttle.LinearThrust)
            {
                thrust += direction;
            }

            var peak = _peakThrust.GetValueOrDefault(grid);
            if (thrust > peak)
                _peakThrust[grid] = peak = thrust;

            var armed = CanShoot(grid);
            if (armed)
                _armed.Add(grid);

            var crewed = IsCrewed(grid);
            if (crewed)
                _crewed.Add(grid);

            reading = new Reading(
                Sustained(grid, Condition.Abandoned, !crewed && _crewed.Contains(grid), now),
                Sustained(grid, Condition.Disarmed, !armed && _armed.Contains(grid), now),
                Sustained(grid, Condition.Crippled, !armed && peak > 0f && thrust < peak * CrippledThrust, now));
        }
        _cache[grid] = (now + CacheTime, reading);
        return reading;
    }

    /// <summary>Whether a condition has held, watched without a gap, for the whole delay.</summary>
    private bool Sustained(EntityUid grid, Condition condition, bool holds, TimeSpan now)
    {
        var key = (grid, condition);
        if (!holds)
        {
            _spells.Remove(key);
            return false;
        }

        var since = _spells.TryGetValue(key, out var spell) && now - spell.Last <= ReadingGap ? spell.Since : now;
        _spells[key] = (since, now);
        return now - since >= DisabledDelay;
    }

    private bool CanShoot(EntityUid grid)
    {
        var weapons = EntityQueryEnumerator<FireControllableComponent, TransformComponent>();
        while (weapons.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && (!TryComp<ApcPowerReceiverComponent>(uid, out var power) || power.Powered))
                return true;
        }

        return false;
    }

    /// <summary>Whether a living player body or crew NPC is aboard.</summary>
    private bool IsCrewed(EntityUid grid)
    {
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && _mobs.IsAlive(uid))
                return true;
        }

        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && _mobs.IsAlive(uid))
                return true;
        }

        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_cache.Count < 64)
            return;

        // Deleted grids never get asked about again; don't keep their entries.
        _cache.Clear();
        _armed.RemoveWhere(grid => TerminatingOrDeleted(grid));
        _crewed.RemoveWhere(grid => TerminatingOrDeleted(grid));
        foreach (var grid in _peakThrust.Keys)
        {
            if (TerminatingOrDeleted(grid))
                _peakThrust.Remove(grid);
        }
        foreach (var grid in _policies.Keys)
        {
            if (TerminatingOrDeleted(grid))
                _policies.Remove(grid);
        }
        foreach (var key in _spells.Keys)
        {
            if (TerminatingOrDeleted(key.Grid))
                _spells.Remove(key);
        }
    }
}
