using System.Numerics;
using System.Linq;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Content.Shared.Projectiles;
using Content.Shared._Mono.Weapons.Ranged.Components;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    [Dependency] private SharedMapSystem _wfShieldMap = default!;
    private readonly HashSet<EntityUid> _wfChangedShieldHulls = new();
    private float _wfHullAccumulator;

    /// <summary>Tracks hull changes without rebuilding every projectile tick.</summary>
    private void InitializeWolfgateShields()
    {
        SubscribeLocalEvent<TileChangedEvent>(OnWolfgateShieldTileChanged);
        SubscribeLocalEvent<ShipShieldComponent, ProjectileReflectAttemptEvent>(OnWolfgateShieldContact);
        SubscribeLocalEvent<ShipShieldedComponent, MoveEvent>(OnWolfgateShieldGridMove);
        SubscribeLocalEvent<ShipShieldedComponent, EntityTerminatingEvent>(OnWolfgateShieldGridTerminating);
    }

    private void OnWolfgateShieldGridMove(EntityUid uid, ShipShieldedComponent component, ref MoveEvent args)
    {
        if (!TerminatingOrDeleted(component.Shield))
            FollowWolfgateShieldGrid(component.Shield, uid);
    }

    private void OnWolfgateShieldGridTerminating(EntityUid uid, ShipShieldedComponent component, ref EntityTerminatingEvent args)
    {
        _wfChangedShieldHulls.Remove(uid);
        TryQueueDel(component.Shield);
    }
    private void OnWolfgateShieldTileChanged(ref TileChangedEvent args)
    {
        if (HasComp<ShipShieldedComponent>(args.Entity) && args.Changes.Any(c => c.OldTile.IsEmpty != c.NewTile.IsEmpty))
            _wfChangedShieldHulls.Add(args.Entity);
    }

    /// <summary>Refreshes changing hulls and emitter health.</summary>
    private void UpdateWolfgateShields(float frameTime)
    {
        var query = EntityQueryEnumerator<WFShipShieldVisualsComponent, ShipShieldComponent>();
        while (query.MoveNext(out var uid, out var visuals, out var shield))
            UpdateWolfgateShieldHealth(uid, visuals, shield.Source);

        _wfHullAccumulator += frameTime;
        if (_wfHullAccumulator < 0.5f)
            return;
        _wfHullAccumulator = 0f;
        foreach (var grid in _wfChangedShieldHulls)
        {
            if (TryComp<ShipShieldedComponent>(grid, out var shielded) &&
                TryComp<MapGridComponent>(grid, out var mapGrid) &&
                TryComp<PhysicsComponent>(shielded.Shield, out var physics))
                CreateWolfgateShieldHull(shielded.Shield, grid, mapGrid, physics);
        }
        _wfChangedShieldHulls.Clear();
    }

    /// <summary>Keeps the perimeter in the map broadphase beyond the grid tile bounds.</summary>
    private void FollowWolfgateShieldGrid(EntityUid shield, EntityUid grid)
    {
        var origin = _transformSystem.ToMapCoordinates(new EntityCoordinates(grid, Vector2.Zero));
        _transformSystem.SetCoordinates(shield, _transformSystem.ToCoordinates(origin));
        _transformSystem.SetWorldRotation(shield, _transformSystem.GetWorldRotation(grid));
    }

    /// <summary>Lets the originating ship's weapon shots cross its own perimeter.</summary>
    private bool IsWolfgateShieldFriendlyProjectile(EntityUid shield, EntityUid projectile)
    {
        return TryComp<ShipShieldComponent>(shield, out var shieldComp) &&
            TryComp<ProjectileGridPhaseComponent>(projectile, out var phase) &&
            phase.SourceGrid == shieldComp.Shielded;
    }
    /// <summary>Uses the padded hull perimeter for both collisions and rendering.</summary>
    private void CreateWolfgateShieldHull(EntityUid shield, EntityUid grid, MapGridComponent mapGrid, PhysicsComponent physics)
    {
        FollowWolfgateShieldGrid(shield, grid);
        var visuals = EnsureComp<WFShipShieldVisualsComponent>(shield);
        var contours = WFShipShieldGeometry.CreateContours(_wfShieldMap.GetAllTiles(grid, mapGrid).Select(t => t.GridIndices), mapGrid.TileSize);
        if (TryComp<FixturesComponent>(shield, out var fixtures))
        {
            foreach (var name in fixtures.Fixtures.Keys.Where(n => n.StartsWith("wfShield", StringComparison.Ordinal)).ToArray())
                _fixtureSystem.DestroyFixture(shield, name, body: physics);
        }
        var index = 0;
        foreach (var contour in contours)
        for (var i = 0; i < contour.Length; i++)
        {
            var edge = new EdgeShape();
            edge.SetOneSided(contour[(i + contour.Length - 1) % contour.Length], contour[i],
                contour[(i + 1) % contour.Length], contour[(i + 2) % contour.Length]);
            _fixtureSystem.TryCreateFixture(shield, edge, $"wfShield{index++}", hard: true,
                collisionLayer: (int) CollisionGroup.BulletImpassable, body: physics);
        }
        visuals.Grid = grid;
        visuals.Contours = contours;
        UpdateWolfgateShieldHealth(shield, visuals, Comp<ShipShieldComponent>(shield).Source);
        Dirty(shield, visuals);
    }

    private void UpdateWolfgateShieldHealth(EntityUid uid, WFShipShieldVisualsComponent visuals, EntityUid? source)
    {
        var health = 1f;
        if (source is { } emitterUid && TryComp<ShipShieldEmitterComponent>(emitterUid, out var emitter))
        {
            var damageFraction = emitter.Damage / Math.Max(emitter.DamageLimit, 1f);
            var loadFraction = CalculateLoadDamage(emitter) / Math.Max(emitter.MaxDraw, 1f);
            health = Math.Clamp(1f - Math.Max(damageFraction, loadFraction), 0f, 1f);
        }
        if (Math.Abs(visuals.Health - health) < 0.001f)
            return;
        visuals.Health = health;
        Dirty(uid, visuals);
    }

    /// <summary>Consumes ship weapon projectiles after a real shield contact.</summary>
    private void OnWolfgateShieldContact(EntityUid uid, ShipShieldComponent component, ref ProjectileReflectAttemptEvent args)
    {
        if (IsWolfgateShieldFriendlyProjectile(uid, args.ProjUid) || !_shipWeaponProjectileQuery.HasComponent(args.ProjUid) ||
            !_projectileQuery.TryGetComponent(args.ProjUid, out var projectile) || projectile.ProjectileSpent)
            return;
        args.Cancelled = true;
        WolfgateShieldImpact(uid, args.ProjUid);
        if (component.Source is { } source)
        {
            var ev = new ShieldDeflectedEvent(args.ProjUid, projectile);
            RaiseLocalEvent(source, ref ev);
        }
        else
        {
            projectile.ProjectileSpent = true;
            QueueDel(args.ProjUid);
        }
    }
    /// <summary>Reports an impact on the visible perimeter.</summary>
    private void WolfgateShieldImpact(EntityUid shield, EntityUid projectile)
    {
        if (!TryComp<WFShipShieldVisualsComponent>(shield, out var visuals))
            return;
        var local = Vector2.Transform(_transformSystem.GetWorldPosition(projectile), _transformSystem.GetInvWorldMatrix(shield));
        var position = WFShipShieldGeometry.ClosestPoint(visuals.Contours, local);
        RaiseNetworkEvent(new WFShipShieldImpactEvent(GetNetEntity(shield), position, 1f), Filter.Pvs(shield));
    }
}
