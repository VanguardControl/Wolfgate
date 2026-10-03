using Content.Server._Mono.FireControl;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Decides when a ship is out of the fight, so crews stop treating it as a threat or a target.</summary>
public sealed partial class WFCrewShipStatusSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How long a ship must stay unable to move or shoot before it counts as disabled.</summary>
    public static readonly TimeSpan DisabledDelay = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(2);
    // A gap this long between readings means the incapable spell was not watched continuously.
    private static readonly TimeSpan ReadingGap = TimeSpan.FromSeconds(5);
    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Disabled)> _cache = new();
    private readonly HashSet<EntityUid> _capable = new();
    private readonly Dictionary<EntityUid, (TimeSpan Since, TimeSpan Last)> _incapable = new();

    /// <summary>
    /// A ship is disabled once it has neither a working thruster nor a powered ship weapon for
    /// <see cref="DisabledDelay"/>. Only a ship seen working earlier is judged; stations, wrecks and asteroids are not.
    /// </summary>
    public bool IsDisabled(EntityUid grid)
    {
        var now = _timing.CurTime;
        if (_cache.TryGetValue(grid, out var cached) && now < cached.Until)
            return cached.Disabled;

        var disabled = false;
        if (!TerminatingOrDeleted(grid) && TryComp<ShuttleComponent>(grid, out var shuttle))
        {
            if (CanMove(shuttle) || CanShoot(grid))
            {
                _capable.Add(grid);
                _incapable.Remove(grid);
            }
            else if (_capable.Contains(grid))
            {
                var since = _incapable.TryGetValue(grid, out var spell) && now - spell.Last <= ReadingGap ? spell.Since : now;
                _incapable[grid] = (since, now);
                disabled = now - since >= DisabledDelay;
            }
        }
        _cache[grid] = (now + CacheTime, disabled);
        return disabled;
    }

    private static bool CanMove(ShuttleComponent shuttle)
    {
        foreach (var thrust in shuttle.LinearThrust)
        {
            if (thrust > 0f)
                return true;
        }

        return false;
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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_cache.Count < 64)
            return;

        // Deleted grids never get asked about again; don't keep their entries.
        _cache.Clear();
        _capable.RemoveWhere(grid => TerminatingOrDeleted(grid));
        foreach (var grid in _incapable.Keys)
        {
            if (TerminatingOrDeleted(grid))
                _incapable.Remove(grid);
        }
    }
}
