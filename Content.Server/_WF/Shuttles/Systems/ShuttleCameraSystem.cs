using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.Shuttles;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Shuttles.Systems;

/// <summary>
/// Lets a pilot look through cameras on the hull instead of their own eyes. The eye rides a camera
/// entity parented to the grid, so FOV is cast from the camera and the hull blocks what it should.
/// </summary>
public sealed partial class ShuttleCameraSystem : EntitySystem
{
    [Dependency] private SharedContentEyeSystem _contentEye = default!;
    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ViewSubscriberSystem _viewSubscriber = default!;

    private static readonly EntProtoId CameraProto = "WFShuttleCamera";

    /// <summary>
    /// How far past the hull edge a camera sits, in tiles, so it never ends up inside a wall.
    /// </summary>
    private const float HullMargin = 0.5f;

    /// <summary>
    /// Zoom the default PVS range already covers. Past it the range grows with the zoom.
    /// </summary>
    private const float PvsFreeZoom = 1.5f;

    /// <summary>
    /// Ceiling on how far the PVS range grows. Its cost goes with the square, so the last of the zoom
    /// trades pop-in at the screen's edges for not loading seven times the area per pilot.
    /// </summary>
    private const float MaxPvsScale = 2f;

    /// <summary>
    /// How often cameras catch up with a changing hull. A ship coming apart loses tiles every tick,
    /// and measuring the hull walks all of them.
    /// </summary>
    private const float HullCheckInterval = 0.5f;

    /// <summary>
    /// Grids that gained or lost hull since the cameras were last placed.
    /// </summary>
    private readonly HashSet<EntityUid> _changedHulls = new();

    private float _hullCheckAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<ShuttleCameraSetMessage>(OnSetMessage);
        });

        SubscribeLocalEvent<ShuttleCameraComponent, ComponentShutdown>(OnCameraShutdown);
        SubscribeLocalEvent<ShuttleCameraComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        // Retiling a floor doesn't move the hull's edge, only tiles appearing or going does.
        foreach (var change in args.Changes)
        {
            if (change.OldTile.IsEmpty == change.NewTile.IsEmpty)
                continue;

            _changedHulls.Add(args.Entity);
            return;
        }
    }

    /// <summary>
    /// Keeps hull cameras on the hull as it's shot away, built on or split. A camera left hanging
    /// where the bow used to be looks straight into whatever is now open to space.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_changedHulls.Count == 0)
            return;

        _hullCheckAccumulator += frameTime;

        if (_hullCheckAccumulator < HullCheckInterval)
            return;

        _hullCheckAccumulator = 0f;

        var query = EntityQueryEnumerator<ShuttleCameraComponent, PilotComponent>();

        while (query.MoveNext(out var pilot, out var comp, out var piloting))
        {
            if (comp.Camera is not { } camera || piloting.Console is not { } console)
                continue;

            // A split can leave the camera on the half the console isn't on, which counts as changed too.
            var cameraGrid = Transform(camera).ParentUid;

            if (!_changedHulls.Contains(cameraGrid) &&
                (!TryGetShuttle(console, out var shuttle) || shuttle.Owner == cameraGrid))
            {
                continue;
            }

            Reposition(pilot, comp, console, camera);
        }

        _changedHulls.Clear();
    }

    /// <summary>
    /// Puts an existing camera back on the hull's edge. Most hull damage is nowhere near that edge,
    /// so this avoids touching the transform, or anything networked, unless the spot really moved.
    /// </summary>
    private void Reposition(EntityUid pilot, ShuttleCameraComponent comp, EntityUid console, EntityUid camera)
    {
        if (!TryGetCameraCoordinates(console, comp.View, out var coords))
        {
            // Nothing left to stand on.
            Apply(pilot, console, ShuttleCameraView.Helm, comp.Zoom, comp.LowLight);
            return;
        }

        var xform = Transform(camera);

        if (xform.ParentUid == coords.EntityId && xform.LocalPosition.EqualsApprox(coords.Position))
            return;

        _transform.SetCoordinates(camera, xform, coords);
    }

    /// <summary>
    /// Called once someone takes the helm, restoring whatever view the console was last left on.
    /// </summary>
    public void OnPilotAdded(EntityUid pilot, Entity<ShuttleConsoleComponent> console)
    {
        var view = ShuttleCameraView.Helm;
        var zoom = console.Comp.Zoom.X;
        var lowLight = false;

        if (TryComp<ShuttleCameraSettingsComponent>(console, out var settings))
        {
            view = settings.View;
            zoom = settings.Zoom ?? zoom;
            lowLight = settings.LowLight;
        }

        Apply(pilot, console, view, zoom, lowLight);
    }

    /// <summary>
    /// Called when someone leaves the helm. The console resets the zoom itself.
    /// </summary>
    public void OnPilotRemoved(EntityUid pilot)
    {
        RemComp<ShuttleCameraComponent>(pilot);
    }

    private void OnSetMessage(Entity<ShuttleConsoleComponent> ent, ref ShuttleCameraSetMessage args)
    {
        if (!TryComp<PilotComponent>(args.Actor, out var pilot) || pilot.Console != ent.Owner)
            return;

        if (!Enum.IsDefined(args.View) || !float.IsFinite(args.Zoom))
            return;

        var settings = EnsureComp<ShuttleCameraSettingsComponent>(ent);
        settings.View = args.View;
        settings.Zoom = Math.Clamp(args.Zoom, ShuttleCameraComponent.MinZoom, ShuttleCameraComponent.MaxZoom);
        settings.LowLight = args.LowLight;

        Apply(args.Actor, ent, settings.View, settings.Zoom.Value, settings.LowLight);
    }

    private void Apply(EntityUid pilot, EntityUid console, ShuttleCameraView view, float zoom, bool lowLight)
    {
        zoom = Math.Clamp(zoom, ShuttleCameraComponent.MinZoom, ShuttleCameraComponent.MaxZoom);

        var comp = EnsureComp<ShuttleCameraComponent>(pilot);
        var pvsScale = Math.Clamp(zoom / PvsFreeZoom, 1f, MaxPvsScale);

        // Measuring the hull walks every tile, so a zoom or low-light change on the same view skips it.
        // Hull changes are picked up separately.
        var placed = view == comp.View && comp.Camera is { } current && !TerminatingOrDeleted(current);

        // Fall back to the helm if the hull can't be measured, rather than leaving the eye nowhere.
        EntityCoordinates coords = default;

        if (view != ShuttleCameraView.Helm && !placed && !TryGetCameraCoordinates(console, view, out coords))
            view = ShuttleCameraView.Helm;

        if (view == ShuttleCameraView.Helm)
        {
            ClearCamera(pilot, comp);
            _eye.SetPvsScale(pilot, pvsScale);
        }
        else
        {
            if (comp.Camera is not { } camera || TerminatingOrDeleted(camera))
            {
                // It hangs off the hull over open space, where traversal would hand it to the map.
                // That has to be off before it's placed, so it can't be spawned straight there.
                camera = Spawn(CameraProto, new EntityCoordinates(coords.EntityId, Vector2.Zero));
                Transform(camera).GridTraversal = false;
                _transform.SetCoordinates(camera, coords);
                comp.Camera = camera;

                if (TryComp<ActorComponent>(pilot, out var actor))
                    _viewSubscriber.AddViewSubscriber(camera, actor.PlayerSession);
            }
            else if (!placed)
            {
                _transform.SetCoordinates(camera, coords);
            }

            // PVS is measured around each viewer, so the range has to grow on the camera, not the pilot.
            _eye.SetPvsScale(camera, pvsScale);
            _eye.SetPvsScale(pilot, 1f);
            _eye.SetTarget(pilot, camera);
        }

        comp.View = view;
        comp.Zoom = zoom;
        comp.LowLight = lowLight;
        Dirty(pilot, comp);

        _contentEye.SetZoom(pilot, new Vector2(zoom), ignoreLimits: true);
    }

    /// <summary>
    /// Where a view's camera sits: just off the hull on that side, lined up with the tiles that
    /// stick out furthest, so a pointed bow puts the camera on the point.
    /// </summary>
    private bool TryGetCameraCoordinates(EntityUid console, ShuttleCameraView view, out EntityCoordinates coords)
    {
        coords = default;

        if (!TryGetShuttle(console, out var shuttle))
            return false;

        var gridUid = shuttle.Owner;
        var grid = shuttle.Comp;

        var dir = view switch
        {
            ShuttleCameraView.Front => Vector2i.Up,
            ShuttleCameraView.Rear => Vector2i.Down,
            ShuttleCameraView.Left => Vector2i.Left,
            ShuttleCameraView.Right => Vector2i.Right,
            _ => Vector2i.Zero,
        };

        if (dir == Vector2i.Zero)
            return false;

        var best = int.MinValue;
        var sideSum = 0f;
        var count = 0;

        foreach (var tile in _map.GetAllTiles(gridUid, grid))
        {
            var along = tile.GridIndices.X * dir.X + tile.GridIndices.Y * dir.Y;

            if (along < best)
                continue;

            if (along > best)
            {
                best = along;
                sideSum = 0f;
                count = 0;
            }

            sideSum += dir.X == 0 ? tile.GridIndices.X : tile.GridIndices.Y;
            count++;
        }

        if (count == 0)
            return false;

        // Tile indices name the tile's low corner, so the far edge is one further out when looking up or right.
        var edge = dir.X + dir.Y > 0 ? best + 1 + HullMargin : -best - HullMargin;
        var side = sideSum / count + 0.5f;
        var local = dir.X == 0 ? new Vector2(side, edge) : new Vector2(edge, side);

        coords = new EntityCoordinates(gridUid, local * grid.TileSize);
        return true;
    }

    /// <summary>
    /// The grid a console flies, which for a drone console isn't the one it's bolted to.
    /// </summary>
    private bool TryGetShuttle(EntityUid console, out Entity<MapGridComponent> shuttle)
    {
        shuttle = default;

        var ev = new ConsoleShuttleEvent { Console = console };
        RaiseLocalEvent(console, ref ev);

        if (ev.Console is not { } source ||
            Transform(source).GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        shuttle = (gridUid, grid);
        return true;
    }

    private void ClearCamera(EntityUid pilot, ShuttleCameraComponent comp)
    {
        if (comp.Camera is not { } camera)
            return;

        comp.Camera = null;

        if (TryComp<EyeComponent>(pilot, out var eye) && eye.Target == camera)
            _eye.SetTarget(pilot, null, eye);

        if (TerminatingOrDeleted(camera))
            return;

        if (TryComp<ActorComponent>(pilot, out var actor))
            _viewSubscriber.RemoveViewSubscriber(camera, actor.PlayerSession);

        QueueDel(camera);
    }

    private void OnCameraShutdown(Entity<ShuttleCameraComponent> ent, ref ComponentShutdown args)
    {
        ClearCamera(ent, ent.Comp);

        // The console resets these when a pilot gets up, but this can go without the console's say,
        // and the helm view widens PVS on the pilot rather than on a camera.
        if (!TerminatingOrDeleted(ent))
            _contentEye.ResetZoom(ent);
    }

    /// <summary>
    /// A view subscription belongs to the session, so it can't outlive the player leaving the body.
    /// </summary>
    private void OnPlayerDetached(Entity<ShuttleCameraComponent> ent, ref PlayerDetachedEvent args)
    {
        if (ent.Comp.Camera is { } camera && !TerminatingOrDeleted(camera))
            _viewSubscriber.RemoveViewSubscriber(camera, args.Player);

        RemCompDeferred<ShuttleCameraComponent>(ent);
    }
}
