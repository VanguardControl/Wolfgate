using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.Parallax;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.Planets;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Caverns;

/// <summary>Cuts the ways down into a cavern (the gate at build, cell and admin mouths) and keeps their registry.</summary>
public sealed partial class WFCavernMouthSystem : EntitySystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How many cells, nearest the gate candidate first, the gate search tries.</summary>
    public const int GateCells = 9;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    /// <summary>The world's gate, if one was claimed.</summary>
    public WFCavernMouth? GetGate(Entity<WFCavernGroundComponent> ground)
    {
        foreach (var mouth in ground.Comp.Mouths)
        {
            if (mouth.Kind == WFCavernMouthKind.Gate)
                return mouth;
        }

        return null;
    }

    /// <summary>The mouth whose hole centre is nearest a ground-local position.</summary>
    public bool TryGetNearestMouth(Entity<WFCavernGroundComponent> ground, Vector2 position, out WFCavernMouth mouth)
    {
        mouth = default;
        var best = float.MaxValue;

        foreach (var candidate in ground.Comp.Mouths)
        {
            var distance = Vector2.DistanceSquared(candidate.Centre, position);
            if (distance >= best)
                continue;

            best = distance;
            mouth = candidate;
        }

        return best < float.MaxValue;
    }

    /// <summary>The mouth cell holding a ground tile.</summary>
    public static Vector2i CellOf(WFCavernMouthSpec spec, Vector2i tile)
    {
        return new Vector2i(FloorDiv(tile.X, spec.CellSize), FloorDiv(tile.Y, spec.CellSize));
    }

    /// <summary>Claims one cell's mouth: stamps its site, or reports why it can't yet (Deferred) or ever (Empty).</summary>
    public WFCavernClaim TryClaimCell(Entity<WFCavernGroundComponent> ground, Vector2i cell)
    {
        return TryGetContext(ground, out var context)
            ? ClaimCell(ground, context, cell, WFCavernMouthKind.Cell)
            : WFCavernClaim.Empty;
    }

    /// <summary>Claims the gate: cells outward from the gate candidate's, up to <see cref="GateCells"/>, until one stamps.</summary>
    public bool ClaimGate(Entity<WFCavernGroundComponent> ground)
    {
        if (!TryGetContext(ground, out var context))
            return false;

        var first = CellOf(context.Spec, GateCandidate(ground, context.Spec));
        var cells = new List<Vector2i>();
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        {
            cells.Add(first + new Vector2i(x, y));
        }

        // The candidate's own cell, then its edge neighbours, then its corners.
        cells.Sort((a, b) => (a - first).LengthSquared.CompareTo((b - first).LengthSquared));

        for (var i = 0; i < cells.Count && i < GateCells; i++)
        {
            if (ClaimCell(ground, context, cells[i], WFCavernMouthKind.Gate) == WFCavernClaim.Claimed)
                return true;
        }

        Log.Warning($"No gate mouth under {ToPrettyString(ground)}: none of the {GateCells} cells around its centre has a site.");
        return false;
    }

    /// <summary>Carves an admin mouth with its hole's bottom-left at a ground tile, clearing biome entities from loaded terrain.</summary>
    /// <param name="refusal">Why it was refused: cavern (either map is gone), grid, built, mob or mouth.</param>
    /// <param name="ignore">A mob allowed to stand in the hole, such as the admin carving it.</param>
    public bool TryOpenMouth(
        Entity<WFCavernGroundComponent> ground,
        Vector2i origin,
        [NotNullWhen(false)] out string? refusal,
        EntityUid? ignore = null)
    {
        refusal = null;

        if (!TryGetContext(ground, out var context))
        {
            refusal = "cavern";
            return false;
        }

        var size = context.Spec.HoleSize;
        var mapId = Comp<MapComponent>(ground).MapId;
        var hole = new Box2(origin, origin + new Vector2i(size, size));

        foreach (var index in Footprint(origin, size))
        {
            if (!ground.Comp.Shades.ContainsKey(index) && !ground.Comp.ClimbPoints.ContainsKey(index))
                continue;

            refusal = "mouth";
            return false;
        }

        var grids = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(mapId, hole.Enlarged(-0.05f), ref grids, approx: true, includeMap: false);
        if (grids.Count > 0)
        {
            refusal = "grid";
            return false;
        }

        foreach (var index in Hole(origin, size))
        {
            foreach (var anchored in _map.GetAnchoredEntities(context.Ground.Owner, context.Ground.Comp2, index))
            {
                if (_biome.WfIsBiomeSpawned((context.Ground.Owner, context.Ground.Comp1), anchored, index))
                    continue;

                refusal = "built";
                return false;
            }
        }

        _mobs.Clear();
        _lookup.GetEntitiesIntersecting(mapId, hole.Enlarged(-0.05f), _mobs);
        foreach (var mob in _mobs)
        {
            if (mob.Owner == ignore || Transform(mob).MapUid != ground.Owner)
                continue;

            refusal = "mob";
            return false;
        }

        Stamp(ground, context, origin, WFCavernMouthKind.Admin);
        return true;
    }

    /// <summary>Floor division, so negative tiles land in negative cells.</summary>
    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }

    /// <summary>Everything a claim or stamp needs about one ground and its cavern.</summary>
    private readonly record struct MouthContext(
        WFCavernPrototype Cavern,
        WFCavernMouthSpec Spec,
        WFPlanetSurfacePrototype Surface,
        Entity<BiomeComponent, MapGridComponent> Ground,
        Entity<BiomeComponent, MapGridComponent> Level,
        HashSet<string> GroundTiles,
        HashSet<string> Avoid);

    /// <summary>Resolves the prototypes and both maps' biomes and grids, or fails if either map is gone.</summary>
    private bool TryGetContext(Entity<WFCavernGroundComponent> ground, out MouthContext context)
    {
        context = default;

        if (!_proto.TryIndex(ground.Comp.Prototype, out var cavern)
            || !_proto.TryIndex(cavern.Surface, out var surface)
            || !TryComp<BiomeComponent>(ground, out var groundBiome)
            || !TryComp<MapGridComponent>(ground, out var groundGrid)
            || !TryComp<BiomeComponent>(ground.Comp.Cavern, out var levelBiome)
            || !TryComp<MapGridComponent>(ground.Comp.Cavern, out var levelGrid))
            return false;

        var spec = cavern.Mouths;
        var tiles = new HashSet<string>();
        foreach (var tile in spec.GroundTiles)
        {
            tiles.Add(tile.Id);
        }

        var avoid = new HashSet<string>();
        foreach (var entity in spec.Avoid)
        {
            avoid.Add(entity.Id);
        }

        context = new MouthContext(
            cavern,
            spec,
            surface,
            (ground.Owner, groundBiome, groundGrid),
            (ground.Comp.Cavern, levelBiome, levelGrid),
            tiles,
            avoid);
        return true;
    }
}
