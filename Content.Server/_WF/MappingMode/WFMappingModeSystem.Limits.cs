using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.GameTicking.Components;
using Content.Shared.Points;
using Content.Shared.Singularity.Components;
using Content.Shared._FarHorizons.StarSystem;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    private readonly WFMappingThrottle<NetUserId> _editThrottle = new(60, 60);
    private readonly WFMappingThrottle<NetUserId> _toolsThrottle = new(10, 10);
    private readonly WFMappingThrottle<NetUserId> _joinThrottle = new(1 / 3.0, 1);
    private readonly WFMappingThrottle<NetUserId> _kickThrottle = new(1, 3);
    private readonly WFMappingThrottle<NetUserId> _listThrottle = new(2, 2);
    private readonly WFMappingThrottle<NetUserId> _pickThrottle = new(1 / 5.0, 1);
    private readonly WFMappingThrottle<NetUserId> _confirmThrottle = new(1, 1);

    /// <summary>How long a counted entity total is trusted before it is counted again.</summary>
    private static readonly TimeSpan CountLifetime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Components that add global PVS overrides on startup, or that only belong to rounds; nothing carrying one
    /// goes onto a mapper's map.
    /// </summary>
    private readonly HashSet<string> _deniedComponents = new();

    private void InitializeLimits()
    {
        _deniedComponents.Add(_factory.GetComponentName<SingularityDistortionComponent>());
        _deniedComponents.Add(_factory.GetComponentName<PointManagerComponent>());
        _deniedComponents.Add(_factory.GetComponentName<GameRuleComponent>());
        _deniedComponents.Add(_factory.GetComponentName<StarSystemMapComponent>());

        Subs.CVar(_cfg, WFMappingModeCVars.MaxEditsPerSecond, rate =>
        {
            _editThrottle.Rate = Math.Max(1, rate);
            _editThrottle.Burst = Math.Max(1, rate);
        }, true);
    }

    private bool TryThrottle(WFMappingThrottle<NetUserId> throttle, ICommonSession player, string what)
    {
        if (throttle.TryTake(player.UserId, _timing.CurTime))
            return true;

        Log.Debug($"Dropped a {what} request from {player}: too many");
        return false;
    }

    /// <summary>Side of the square every tile must lie in, in tiles.</summary>
    public int MapSize => Math.Clamp(_cfg.GetCVar(WFMappingModeCVars.MapSize), 64, 512);

    /// <summary>Whether a map position lies on a tile inside the map area.</summary>
    private bool InBounds(Vector2 position)
    {
        var half = MapSize / 2;
        var x = (int) MathF.Floor(position.X);
        var y = (int) MathF.Floor(position.Y);
        return x >= -half && x < half && y >= -half && y < half;
    }

    /// <summary>Whether a box in map coordinates lies inside the map area.</summary>
    private bool InBounds(Box2 box)
    {
        const float epsilon = 0.01f;
        var half = MapSize / 2f + epsilon;
        return box.Left >= -half && box.Bottom >= -half && box.Right <= half && box.Top <= half;
    }

    /// <summary>Whether cells, local to <paramref name="grid"/> or map tiles without one, lie inside the map area.</summary>
    private bool CellsInBounds(EntityUid? grid, Box2i cells)
    {
        var box = new Box2(cells.Left, cells.Bottom, cells.Right, cells.Top);
        if (grid is { } gridUid)
            box = _transform.GetWorldMatrix(gridUid).TransformBox(box);

        return InBounds(box);
    }

    /// <summary>How many grids a session may have: a share per member, up to the session cap.</summary>
    public int GridCap(WFMappingSession session)
    {
        var perPlayer = _cfg.GetCVar(WFMappingModeCVars.GridsPerPlayer);
        return Math.Min(perPlayer * Math.Max(1, session.Members.Count),
            _cfg.GetCVar(WFMappingModeCVars.MaxGridsPerSession));
    }

    public int CountGrids(WFMappingSession session)
    {
        var count = 0;
        foreach (var map in SessionMaps(session.Id))
        {
            var children = Transform(map).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (HasComp<MapGridComponent>(child))
                    count++;
            }
        }

        return count;
    }

    /// <summary>Entities across the session's maps, counted at most once a second.</summary>
    public int EntityCount(WFMappingSession session)
    {
        var now = _timing.CurTime;
        if (now - session.CountedAt < CountLifetime)
            return session.EntityCount;

        var count = 0;
        foreach (var map in SessionMaps(session.Id))
        {
            count += CountTree(map) - 1;
        }

        session.EntityCount = count;
        session.CountedAt = now;
        return count;
    }

    /// <summary>An entity and everything under it.</summary>
    private int CountTree(EntityUid root)
    {
        var count = 0;
        var stack = new Stack<EntityUid>();
        stack.Push(root);
        while (stack.TryPop(out var uid))
        {
            count++;
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                stack.Push(child);
            }
        }

        return count;
    }

    /// <summary>
    /// Entities whose world position falls in a cell of a grid, whatever they are parented to. Entities in
    /// <paramref name="moving"/>, and anything under them, aren't counted.
    /// </summary>
    private int EntitiesOnTile(EntityUid grid, Vector2i cell, HashSet<EntityUid>? moving = null)
    {
        var count = 0;
        var invMatrix = _transform.GetInvWorldMatrix(grid);
        foreach (var uid in _lookup.GetLocalEntitiesIntersecting(grid, cell, 0f))
        {
            if (uid == grid || moving != null && UnderAny(uid, grid, moving))
                continue;

            var local = Vector2.Transform(_transform.GetWorldPosition(uid), invMatrix);
            if (MappingToolsMath.CellOf(local) == cell)
                count++;
        }

        return count;
    }

    /// <summary>Whether an entity, or a parent of it below <paramref name="stop"/>, is in <paramref name="set"/>.</summary>
    private bool UnderAny(EntityUid uid, EntityUid stop, HashSet<EntityUid> set)
    {
        while (uid.IsValid() && uid != stop)
        {
            if (set.Contains(uid))
                return true;

            uid = Transform(uid).ParentUid;
        }

        return false;
    }

    /// <summary>Decals on a grid whose centre falls in the cell.</summary>
    private int DecalsOnTile(EntityUid grid, Vector2i cell)
    {
        var count = 0;
        var centre = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
        foreach (var (_, decal) in _decals.GetDecalsInRange(grid, centre, 1.5f))
        {
            if (MappingToolsMath.CellOf(decal.Coordinates + new Vector2(0.5f)) == cell)
                count++;
        }

        return count;
    }

    private bool IsDenied(EntityPrototype prototype)
    {
        return prototype.Abstract ||
               prototype.HideSpawnMenu ||
               _deniedComponents.Any(prototype.Components.ContainsKey);
    }

    private bool HasDeniedComponent(EntityUid uid)
    {
        foreach (var name in _deniedComponents)
        {
            if (HasComp(uid, _factory.GetRegistration(name).Type))
                return true;
        }

        return false;
    }

    public WFMappingLimitsState GetLimits(WFMappingSession session)
    {
        return new WFMappingLimitsState(CountGrids(session), GridCap(session), EntityCount(session),
            _cfg.GetCVar(WFMappingModeCVars.MaxEntities), MapSize);
    }

    /// <summary>
    /// The size limits of one mapping tools request: cells selected, entities touched and decals carried.
    /// </summary>
    private bool CheckWorkLimits(WFMappingCanEditEvent ev)
    {
        var maxCells = _cfg.GetCVar(WFMappingModeCVars.MaxSelectionCells);
        if (ev.SelectionCells > maxCells)
        {
            Cancel(ev, "wf-mapping-mode-limit-selection", ("max", maxCells));
            return false;
        }

        var maxEntities = _cfg.GetCVar(WFMappingModeCVars.MaxEditEntities);
        if (ev.TouchedEntities > maxEntities)
        {
            Cancel(ev, "wf-mapping-mode-limit-edit-entities", ("max", maxEntities));
            return false;
        }

        var maxDecals = _cfg.GetCVar(WFMappingModeCVars.MaxEditDecals);
        if (ev.Decals > maxDecals || ev.DecalCells?.Count > maxDecals)
        {
            Cancel(ev, "wf-mapping-mode-limit-edit-decals", ("max", maxDecals));
            return false;
        }

        return true;
    }

    /// <summary>
    /// The area, grid, entity, per-tile entity and per-tile decal limits of a mapping tools edit or paste.
    /// </summary>
    private void CheckEditLimits(WFMappingCanEditEvent ev, WFMappingSession session)
    {
        if (ev.Cells is { } cells && !CellsInBounds(ev.Grid, cells))
        {
            Cancel(ev, "wf-mapping-mode-limit-area", ("size", MapSize));
            return;
        }

        if (ev.NewGrid)
        {
            var cap = GridCap(session);
            if (CountGrids(session) >= cap)
            {
                Cancel(ev, "wf-mapping-mode-limit-grids", ("max", cap));
                return;
            }
        }

        var maxEntities = _cfg.GetCVar(WFMappingModeCVars.MaxEntities);
        if (ev.Entities > 0 && EntityCount(session) + ev.Entities > maxEntities)
        {
            Cancel(ev, "wf-mapping-mode-limit-entities", ("max", maxEntities));
            return;
        }

        if (ev.DecalCells is { Count: > 0 } decalCells)
        {
            var maxDecals = _cfg.GetCVar(WFMappingModeCVars.MaxDecalsPerTile);
            foreach (var group in decalCells.GroupBy(c => c))
            {
                var existing = ev.Grid is { } decalGrid ? DecalsOnTile(decalGrid, group.Key) : 0;
                if (existing + group.Count() > maxDecals)
                {
                    Cancel(ev, "wf-mapping-mode-limit-decals-per-tile", ("max", maxDecals));
                    return;
                }
            }
        }

        if (ev.EntityCells is not { Count: > 0 } entityCells)
            return;

        var maxPerTile = _cfg.GetCVar(WFMappingModeCVars.MaxEntitiesPerTile);
        foreach (var group in entityCells.GroupBy(c => c))
        {
            var existing = ev.Grid is { } grid ? EntitiesOnTile(grid, group.Key, ev.Moving) : 0;
            if (existing + group.Count() > maxPerTile)
            {
                Cancel(ev, "wf-mapping-mode-limit-entities-per-tile", ("max", maxPerTile));
                return;
            }
        }
    }

    /// <summary>
    /// The engine placement gate for mappers: placing, laying tiles and erasing, only on their session's maps and
    /// within its limits. Admins are let through before this by the sandbox's own check.
    /// </summary>
    public bool AllowPlacement(ICommonSession player, MsgPlacement msg)
    {
        if (!Enabled || !TryGetPresent(player, out var session))
            return false;

        switch (msg.PlaceType)
        {
            case PlacementManagerMessage.StartPlacement:
            case PlacementManagerMessage.CancelPlacement:
                return true;
            case PlacementManagerMessage.RequestPlacement:
                return TryThrottle(_editThrottle, player, "placement") && AllowPlace(player, session, msg);
            case PlacementManagerMessage.RequestEntRemove:
                return TryThrottle(_editThrottle, player, "erase") && AllowErase(player, session, msg);
            case PlacementManagerMessage.RequestRectRemove:
                return TryThrottle(_editThrottle, player, "erase") && AllowRectErase(player, session, msg);
            default:
                return false;
        }
    }

    private bool AllowPlace(ICommonSession player, WFMappingSession session, MsgPlacement msg)
    {
        var coords = GetCoordinates(msg.NetCoordinates);
        if (!coords.IsValid(EntityManager) || _transform.GetMap(coords) is not { } map || !IsSessionMap(map, session))
        {
            Log.Info($"Dropped a placement from {player} at {msg.NetCoordinates}, off their session's maps");
            Refuse(player, "wf-mapping-mode-not-your-map");
            return false;
        }

        var mapCoords = _transform.ToMapCoordinates(coords);
        if (!InBounds(mapCoords.Position))
        {
            Refuse(player, "wf-mapping-mode-limit-area", ("size", MapSize));
            return false;
        }

        EntityUid? gridUid = null;
        MapGridComponent? grid = null;
        if (TryComp(coords.EntityId, out MapGridComponent? own))
            (gridUid, grid) = (coords.EntityId, own);
        else if (_map.TryFindGridAt(mapCoords, out var found, out var foundGrid))
            (gridUid, grid) = (found, foundGrid);

        if (msg.IsTile)
        {
            if (!_tileDefs.TryGetDefinition(msg.TileType, out _))
            {
                Refuse(player, "wf-mapping-mode-bad-tile");
                return false;
            }

            if (gridUid == null && msg.TileType != 0)
            {
                var cap = GridCap(session);
                if (CountGrids(session) >= cap)
                {
                    Refuse(player, "wf-mapping-mode-limit-grids", ("max", cap));
                    return false;
                }
            }

            return true;
        }

        if (!_prototypes.TryIndex<EntityPrototype>(msg.EntityTemplateName ?? string.Empty, out var prototype) ||
            IsDenied(prototype))
        {
            Refuse(player, "wf-mapping-mode-limit-prototype");
            return false;
        }

        var maxEntities = _cfg.GetCVar(WFMappingModeCVars.MaxEntities);
        if (EntityCount(session) + 1 > maxEntities)
        {
            Refuse(player, "wf-mapping-mode-limit-entities", ("max", maxEntities));
            return false;
        }

        // Mappers build on grids; an entity in space would escape the per-tile count.
        if (gridUid is not { } g || grid == null)
        {
            Refuse(player, "wf-mapping-mode-limit-off-grid");
            return false;
        }

        var maxPerTile = _cfg.GetCVar(WFMappingModeCVars.MaxEntitiesPerTile);
        var cell = _map.TileIndicesFor(g, grid, mapCoords);
        if (EntitiesOnTile(g, cell) >= maxPerTile)
        {
            Refuse(player, "wf-mapping-mode-limit-entities-per-tile", ("max", maxPerTile));
            return false;
        }

        session.EntityCount++;
        return true;
    }

    private bool AllowErase(ICommonSession player, WFMappingSession session, MsgPlacement msg)
    {
        if (!TryGetEntity(msg.EntityUid, out var target) || TerminatingOrDeleted(target))
            return false;

        if (Transform(target.Value).MapUid is not { } map || !IsSessionMap(map, session))
        {
            Log.Info($"Dropped an erase of {ToPrettyString(target.Value)} from {player}, off their session's maps");
            Refuse(player, "wf-mapping-mode-not-your-map");
            return false;
        }

        if (HasComp<MapComponent>(target) || HoldsPlayer(target.Value))
        {
            Refuse(player, "wf-mapping-mode-erase-refused");
            return false;
        }

        return true;
    }

    private bool AllowRectErase(ICommonSession player, WFMappingSession session, MsgPlacement msg)
    {
        if (!TryGetEntity(msg.NetCoordinates.NetEntity, out var parent) ||
            Transform(parent.Value).MapUid is not { } map ||
            !IsSessionMap(map, session))
        {
            Log.Info($"Dropped an area erase from {player} at {msg.NetCoordinates}, off their session's maps");
            Refuse(player, "wf-mapping-mode-not-your-map");
            return false;
        }

        // The engine takes the box as sent, so its size is checked here.
        var size = msg.RectSize;
        var maxCells = _cfg.GetCVar(WFMappingModeCVars.MaxSelectionCells);
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X < 0f || size.Y < 0f ||
            (double) MathF.Ceiling(size.X) * MathF.Ceiling(size.Y) > maxCells)
        {
            Refuse(player, "wf-mapping-mode-limit-selection", ("max", maxCells));
            return false;
        }

        var centre = _transform.ToMapCoordinates(GetCoordinates(msg.NetCoordinates));
        var box = new Box2Rotated(Box2.CenteredAround(centre.Position, size), msg.RectRotation, centre.Position);
        var maxEntities = _cfg.GetCVar(WFMappingModeCVars.MaxEditEntities);
        if (_lookup.GetEntitiesIntersecting(centre.MapId, box).Count > maxEntities)
        {
            Refuse(player, "wf-mapping-mode-limit-edit-entities", ("max", maxEntities));
            return false;
        }

        return true;
    }

    /// <summary>Whether an entity is, or carries, a player or a mapping observer.</summary>
    private bool HoldsPlayer(EntityUid target)
    {
        if (HasComp<ActorComponent>(target) || HasComp<WFMappingObserverComponent>(target))
            return true;

        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out _, out _, out var xform))
        {
            var parent = xform.ParentUid;
            while (parent.IsValid())
            {
                if (parent == target)
                    return true;

                parent = Transform(parent).ParentUid;
            }
        }

        return false;
    }
}
