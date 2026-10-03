using Content.Server._Mono.FireControl;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Decides when a ship is out of the fight, so crews stop treating it as a threat or a target.</summary>
public sealed partial class WFCrewShipStatusSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;

    /// <summary>How long a ship must stay out of the fight before it counts as disabled.</summary>
    public static readonly TimeSpan DisabledDelay = TimeSpan.FromSeconds(10);

    /// <summary>A ship with less than this share of the most thrust it was seen with cannot manoeuvre.</summary>
    private const float CrippledThrust = 0.25f;

    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(2);
    // A gap this long between readings means the spell was not watched continuously.
    private static readonly TimeSpan ReadingGap = TimeSpan.FromSeconds(5);
    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Disabled)> _cache = new();
    private readonly Dictionary<EntityUid, float> _peakThrust = new();
    private readonly HashSet<EntityUid> _armed = new();
    private readonly HashSet<EntityUid> _crewed = new();
    private readonly Dictionary<EntityUid, (TimeSpan Since, TimeSpan Last)> _incapable = new();

    /// <summary>
    /// A ship is disabled once, for <see cref="DisabledDelay"/> without a break, any of these holds: everyone who
    /// was aboard is dead or gone; it had ship weapons and none is powered now; or it has no powered weapon and
    /// under a quarter of its thrust. Each is judged against what the ship was seen with earlier, so stations,
    /// wrecks, asteroids and drones are never disabled for lacking what they never had.
    /// </summary>
    public bool IsDisabled(EntityUid grid)
    {
        var now = _timing.CurTime;
        if (_cache.TryGetValue(grid, out var cached) && now < cached.Until)
            return cached.Disabled;

        var disabled = false;
        if (!TerminatingOrDeleted(grid) && TryComp<ShuttleComponent>(grid, out var shuttle))
        {
            if (OutOfFight(grid, shuttle))
            {
                var since = _incapable.TryGetValue(grid, out var spell) && now - spell.Last <= ReadingGap ? spell.Since : now;
                _incapable[grid] = (since, now);
                disabled = now - since >= DisabledDelay;
            }
            else
                _incapable.Remove(grid);
        }
        _cache[grid] = (now + CacheTime, disabled);
        return disabled;
    }

    private bool OutOfFight(EntityUid grid, ShuttleComponent shuttle)
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

        if (!crewed && _crewed.Contains(grid))
            return true;
        if (armed)
            return false;
        return _armed.Contains(grid) || peak > 0f && thrust < peak * CrippledThrust;
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
        foreach (var grid in _incapable.Keys)
        {
            if (TerminatingOrDeleted(grid))
                _incapable.Remove(grid);
        }
    }
}
