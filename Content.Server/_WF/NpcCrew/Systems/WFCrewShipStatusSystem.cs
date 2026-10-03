using Content.Server._Mono.FireControl;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Decides when a ship is out of the fight, so crews stop treating it as a threat or a target.</summary>
public sealed partial class WFCrewShipStatusSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(2);
    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Disabled)> _cache = new();
    private readonly HashSet<EntityUid> _capable = new();

    /// <summary>
    /// A ship is disabled once it can neither move nor shoot: no working thruster and no powered ship weapon.
    /// Only a ship seen working earlier can become disabled; stations, wrecks and asteroids are not judged.
    /// </summary>
    public bool IsDisabled(EntityUid grid)
    {
        if (_cache.TryGetValue(grid, out var cached) && _timing.CurTime < cached.Until)
            return cached.Disabled;

        var disabled = false;
        if (!TerminatingOrDeleted(grid) && TryComp<ShuttleComponent>(grid, out var shuttle))
        {
            if (CanMove(shuttle) || CanShoot(grid))
                _capable.Add(grid);
            else
                disabled = _capable.Contains(grid);
        }
        _cache[grid] = (_timing.CurTime + CacheTime, disabled);
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
    }
}
