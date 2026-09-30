using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Shared.Damage;
using Content.Shared.Weapons.Hitscan.Components;
using Robust.Shared.Player;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    [Dependency] private DamageableSystem _wfHitscanDamage = default!;

    /// <summary>Uses ordinary shield capacity, allocation and impact effects for intercepted beams.</summary>
    public void ApplyWolfgateHitscanImpact(EntityUid shield, EntityUid hitscan, Vector2 position, float strength)
    {
        if (strength <= 0f || !TryComp<ShipShieldComponent>(shield, out var component))
            return;
        if (component.Source is { } source && TryComp<ShipShieldEmitterComponent>(source, out var emitter) &&
            TryComp<HitscanBasicDamageComponent>(hitscan, out var damage))
        {
            emitter.Damage += MathF.Max(0f, (float)(damage.Damage * _wfHitscanDamage.UniversalHitscanDamageModifier).GetTotal()) / strength;
            if (TryComp<WFShipShieldVisualsComponent>(shield, out var visuals))
                UpdateWolfgateShieldHealth(shield, visuals, source);
        }
        PlayWolfgateShieldImpact(shield, position);
        RaiseNetworkEvent(new WFShipShieldImpactEvent(GetNetEntity(shield), position, 1f), Filter.Broadcast());
    }
}
