using Robust.Shared.Timing; // WOLFGATE(Performance)
using Content.Server._Mono.Temperature.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._WF.Performance; // WOLFGATE(Performance)
using Content.Shared.IgnitionSource;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Server._Mono.Temperature.Systems;

/// <summary>
/// Gives thermal energy to nearby entities.
/// </summary>
public sealed partial class EntityRadiusHeaterSystem : EntitySystem
{
    [Dependency] private TemperatureSystem _temp = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    // WOLFGATE(Performance) START: each heater takes its own tick of the second, not all on one
    [Dependency] private IGameTiming _timing = default!;

    private float _updateCooldown = 1f;
    // private TimeSpan _updateTimer = TimeSpan.Zero;

    public override void Update(float frameTime)
    {
        // if (_updateTimer < TimeSpan.FromSeconds(_updateCooldown))
        // {
        //     _updateTimer += TimeSpan.FromSeconds(frameTime);
        //     return;
        // }
        var period = WFTickSpread.Period(_timing, _updateCooldown);
        var tick = _timing.CurTick;

        var eqe = EntityQueryEnumerator<EntityRadiusHeaterComponent>();

        // while (eqe.MoveNext(out var uid, out var comp))
        while (WFNextDue(ref eqe, tick, period, out var uid, out var comp))
        // WOLFGATE END
        {
            if (comp.RequireActivation && TryComp<IgnitionSourceComponent>(uid, out var ignite)
                                       && !ignite.Ignited)
                continue;

            if (comp.RequireActivation && TryComp<ItemToggleComponent>(uid, out var toggle)
                                       && !toggle.Activated)
                continue;

            if (!this.IsPowered(uid, EntityManager))
                continue;

            // WOLFGATE(Performance) START: what a fire warms is loose; skipping anchored and stored things skips the trees round a planet's fires
            // var nearby = _lookup.GetEntitiesInRange<TemperatureComponent>(Transform(uid).Coordinates, comp.Radius);
            var nearby = _lookup.GetEntitiesInRange<TemperatureComponent>(Transform(uid).Coordinates, comp.Radius, LookupFlags.Dynamic | LookupFlags.Sundries);
            // WOLFGATE END
            var xform = Transform(uid);
            foreach (var ent in nearby)
            {
                _temp.ChangeHeat(ent, CalculateThermalEnergy(ent, xform, comp));
            }
        }


        // _updateTimer = TimeSpan.Zero; // WOLFGATE(Performance): each heater takes its own tick
    }

    public float CalculateThermalEnergy(Entity<TemperatureComponent> ent,
        TransformComponent heaterXform,
        EntityRadiusHeaterComponent comp)
    {
        if (!Transform(ent).Coordinates.TryDistance(EntityManager, heaterXform.Coordinates, out var distance))
            return 0f;

        var c = 1 - (distance / comp.Radius);

        if (c < 0)
            return 0;

        var oT = _temp.GetHeatCapacity(ent) * ent.Comp.CurrentTemperature;
        var nT = Math.Clamp(c * comp.ThermalEnergy + oT, 0, _temp.GetHeatCapacity(ent) * comp.Limit);

        var d = nT - oT;

        if (d < 0)
            return 0;

        return d;
    }
}
