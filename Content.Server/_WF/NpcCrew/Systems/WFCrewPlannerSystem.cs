using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>One planned post: where a crewman of a role stands, and what the planner saw there.</summary>
public readonly record struct WFCrewPost(
    EntityCoordinates Coordinates,
    ProtoId<WFCrewRolePrototype> Role,
    WFCrewPostKind Kind);

public enum WFCrewPostKind : byte
{
    Marker,
    Helm,
    Radio,
    Dock,
    Deck,
}

/// <summary>
/// Decides where crew go on a grid. Spawn markers win when the map has any; otherwise a pilot beside the helm, a
/// radio officer beside that, a deckhand inside each airlock and the rest on the most open deck tiles, spread out.
/// </summary>
public sealed class WFCrewPlannerSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;

    /// <summary>Minimum Chebyshev distance in tiles between deck posts and anything else.</summary>
    private const int DeckSpacing = 3;

    private static readonly Vector2i[] Neighbours =
    {
        new(0, -1), new(1, 0), new(0, 1), new(-1, 0),
    };

    public List<WFCrewPost> Plan(EntityUid grid, int deckhands = 2)
    {
        var posts = new List<WFCrewPost>();
        if (!TryComp(grid, out MapGridComponent? found))
            return posts;

        // A non-nullable copy for the local function below, which can't see the null check.
        MapGridComponent gridComp = found;

        // Mapper intent wins.
        var markers = EntityQueryEnumerator<WFCrewSpawnPointComponent, TransformComponent>();
        while (markers.MoveNext(out _, out var marker, out var xform))
        {
            if (xform.GridUid == grid)
                posts.Add(new WFCrewPost(xform.Coordinates, marker.Role, WFCrewPostKind.Marker));
        }

        if (posts.Count > 0)
            return posts;

        var free = FreeTiles(grid, gridComp);
        var taken = new HashSet<Vector2i>();

        // The helm, and a radio post beside it.
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;

            var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            if (!TryNeighbour(tile, free, taken, out var helm))
                continue;

            Add(helm, WFCrewRoles.Pilot, WFCrewPostKind.Helm);
            if (TryNeighbour(tile, free, taken, out var radio))
                Add(radio, WFCrewRoles.RadioOperator, WFCrewPostKind.Radio);
            break;
        }

        // The first free tile inside each airlock.
        var docks = EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;

            var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            // A dock mates along its local -Y, so inward is the other way.
            var outward = xform.LocalRotation.RotateVec(new Vector2(0f, -1f));
            var inward = new Vector2i((int) MathF.Round(-outward.X), (int) MathF.Round(-outward.Y));
            for (var i = 1; i <= 3; i++)
            {
                var candidate = new Vector2i(tile.X + inward.X * i, tile.Y + inward.Y * i);
                if (!free.Contains(candidate) || taken.Contains(candidate))
                    continue;

                Add(candidate, WFCrewRoles.Deckhand, WFCrewPostKind.Dock);
                break;
            }
        }

        // Open deck: the most open tiles, kept apart.
        foreach (var tile in free.OrderByDescending(t => Openness(t, free)).ThenBy(t => t.X).ThenBy(t => t.Y))
        {
            if (deckhands <= 0)
                break;

            if (taken.Any(t => Chebyshev(t, tile) < DeckSpacing))
                continue;

            Add(tile, WFCrewRoles.Deckhand, WFCrewPostKind.Deck);
            deckhands--;
        }

        return posts;

        void Add(Vector2i tile, ProtoId<WFCrewRolePrototype> role, WFCrewPostKind kind)
        {
            taken.Add(tile);
            posts.Add(new WFCrewPost(_map.GridTileToLocal(grid, gridComp, tile), role, kind));
        }
    }

    /// <summary>Tiles a mob can stand on.</summary>
    private HashSet<Vector2i> FreeTiles(EntityUid grid, MapGridComponent gridComp)
    {
        var free = new HashSet<Vector2i>();
        var tiles = _map.GetAllTilesEnumerator(grid, gridComp);
        while (tiles.MoveNext(out var tile))
        {
            if (!_turf.IsTileBlocked(grid, tile.Value.GridIndices, CollisionGroup.MobMask, gridComp))
                free.Add(tile.Value.GridIndices);
        }

        return free;
    }

    private static bool TryNeighbour(Vector2i tile, HashSet<Vector2i> free, HashSet<Vector2i> taken, out Vector2i found)
    {
        foreach (var dir in Neighbours)
        {
            var candidate = tile + dir;
            if (!free.Contains(candidate) || taken.Contains(candidate))
                continue;

            found = candidate;
            return true;
        }

        found = default;
        return false;
    }

    /// <summary>Free tiles within two of the tile, itself included; 25 is the middle of an open room.</summary>
    private static int Openness(Vector2i tile, HashSet<Vector2i> free)
    {
        var count = 0;
        for (var x = -2; x <= 2; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                if (free.Contains(new Vector2i(tile.X + x, tile.Y + y)))
                    count++;
            }
        }

        return count;
    }

    private static int Chebyshev(Vector2i a, Vector2i b)
    {
        return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}
