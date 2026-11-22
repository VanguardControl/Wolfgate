using Content.Server.Atmos.Rotting;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Power.Components;
using Robust.Shared.Configuration;

namespace Content.Server._WG.ColdStorage;

/// <summary>Raised when a grid comes out of stasis or the cold store, with how long it was away.</summary>
[ByRefEvent]
public readonly record struct WGGridCatchUpEvent(EntityUid Grid, TimeSpan Elapsed);

/// <summary>
/// Applies the time a grid spent frozen or stored to what keeps going with nobody there: rot, plants and batteries
/// that recharge themselves. A ship is only frozen with its power off, so nothing that needs power is caught up.
/// </summary>
public sealed class WGCatchUpSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private RottingSystem _rotting = default!;
    [Dependency] private PlantHolderSystem _plants = default!;
    [Dependency] private BatterySystem _battery = default!;

    /// <summary>Shorter absences are not worth a walk over the grid.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(60);

    /// <summary>Most growth cycles a plant is given at once: an hour's worth at the usual 15 s.</summary>
    public const int MaxPlantCycles = 240;

    private readonly List<EntityUid> _walk = new();
    private bool _enabled;

    /// <summary>Grids caught up so far, and the seconds they had been away; for tests and the census.</summary>
    public int CaughtUp { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, WGColdStoreCVars.CatchUp, value => _enabled = value, true);
    }

    /// <summary>Applies an absence to everything on a grid. Does nothing for one under a minute.</summary>
    public void CatchUp(EntityUid grid, TimeSpan elapsed)
    {
        if (!_enabled || elapsed < Shortest || TerminatingOrDeleted(grid))
            return;

        CaughtUp++;
        _walk.Clear();
        _walk.Add(grid);
        for (var i = 0; i < _walk.Count; i++)
        {
            var uid = _walk[i];
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _walk.Add(child);
            }

            if (uid != grid)
                CatchUpEntity(uid, elapsed);
        }

        _walk.Clear();

        var ev = new WGGridCatchUpEvent(grid, elapsed);
        RaiseLocalEvent(grid, ref ev, true);
    }

    private void CatchUpEntity(EntityUid uid, TimeSpan elapsed)
    {
        if (HasComp<PerishableComponent>(uid))
            _rotting.WGAdvanceRot(uid, elapsed);

        if (TryComp<PlantHolderComponent>(uid, out var plant) && plant.Seed != null && plant.CycleDelay > TimeSpan.Zero)
        {
            var cycles = (int) Math.Min(MaxPlantCycles, elapsed.TotalSeconds / plant.CycleDelay.TotalSeconds);
            for (var i = 0; i < cycles && plant.Seed != null; i++)
            {
                plant.ForceUpdate = true;
                _plants.Update(uid, plant);
            }
        }

        if (TryComp<BatterySelfRechargerComponent>(uid, out var recharger)
            && recharger.AutoRecharge
            && recharger.AutoRechargeRate > 0f
            && TryComp<BatteryComponent>(uid, out var battery)
            && battery.CurrentCharge < battery.MaxCharge)
        {
            var charge = battery.CurrentCharge + recharger.AutoRechargeRate * (float) elapsed.TotalSeconds;
            _battery.SetCharge(uid, MathF.Min(battery.MaxCharge, charge), battery);
        }
    }
}
