using System.Numerics;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Item;
using Content.Shared.Light.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.WolfWeather;

/// <summary>Pushes loose items on exposed tiles with the wind.</summary>
public sealed partial class WindPushSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;
    [Dependency] private EntityQuery<WolfWeatherMapComponent> _weatherQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<RoofComponent> _roofQuery = default!;
    [Dependency] private EntityQuery<WindPushableComponent> _pushableQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;

    private readonly HashSet<Entity<ItemComponent>> _items = new();
    private readonly HashSet<EntityUid> _pushed = new();
    private TimeSpan _nextPush;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextPush)
            return;

        var interval = 0.5f;
        _pushed.Clear();

        EntityUid? settingsMap = null;
        WolfWeatherSettingsPrototype? settings = null;

        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid is not { } mapUid || !_weatherQuery.TryComp(mapUid, out var weather))
                continue;

            if (settingsMap != mapUid)
            {
                settingsMap = mapUid;
                settings = _proto.TryIndex(weather.Settings, out var found) ? found : null;
            }

            if (settings is not { PushEnabled: true })
                continue;

            interval = settings.PushInterval;
            var wind = weather.Wind;
            var speed = wind.Length();
            if (speed <= settings.PushThreshold)
                continue;

            var direction = wind / speed;
            var over = speed - settings.PushThreshold;
            var chance = Math.Clamp(settings.PushChance * over / settings.PushThreshold, 0f, 1f);

            _items.Clear();
            _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(xform), settings.PushRadius, _items, LookupFlags.Dynamic | LookupFlags.Sundries);

            foreach (var item in _items)
            {
                if (!_pushed.Add(item.Owner) || !_random.Prob(chance))
                    continue;

                var factor = 1f;
                if (_pushableQuery.TryComp(item.Owner, out var pushable))
                {
                    if (!pushable.Enabled)
                        continue;

                    factor = pushable.Factor;
                }
                else if (_item.GetSizePrototype(item.Comp.Size).Weight > settings.PushMaxWeight)
                {
                    continue;
                }

                if (!_physicsQuery.TryComp(item.Owner, out var body) || !IsExposed(item.Owner))
                    continue;

                var jitter = new Vector2(-direction.Y, direction.X) * _random.NextFloat(-0.15f, 0.15f);
                var push = (direction + jitter) * over * settings.PushStrength * factor * _random.NextFloat(0.5f, 1.5f);
                _physics.ApplyLinearImpulse(item.Owner, push * body.Mass, body: body);
            }
        }

        _nextPush = now + TimeSpan.FromSeconds(interval);
    }

    private bool IsExposed(EntityUid uid)
    {
        var xform = _xformQuery.GetComponent(uid);
        if (xform.Anchored)
            return false;

        if (xform.GridUid is not { } gridUid || !_gridQuery.TryComp(gridUid, out var grid))
            return true;

        _roofQuery.TryComp(gridUid, out var roof);
        var tile = _map.GetTileRef(gridUid, grid, xform.Coordinates);
        return _wolfWeather.IsExposed((gridUid, grid, roof), tile);
    }
}
