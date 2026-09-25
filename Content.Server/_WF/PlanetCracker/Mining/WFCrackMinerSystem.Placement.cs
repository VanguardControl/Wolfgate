using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Mining;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Construction.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Mining;

/// <summary>The placement gate - chunk grid plus a vein in the miner's own snap cell - and the three lookups it is built from.</summary>
public sealed partial class WFCrackMinerSystem
{
    /// <summary>Resolves the chunk grid an entity is standing on, or fails on a hull, a planet layer or a map.</summary>
    public bool TryGetChunk(
        TransformComponent xform,
        out Entity<WFPlanetChunkComponent> chunk,
        out Entity<MapGridComponent> grid)
    {
        chunk = default;
        grid = default;

        // A chunk is a grid on a map, never the map itself, so unlike TryGetPlanetGround there's no MapUid test.
        if (xform.GridUid is not { } gridUid)
            return false;

        if (!TryComp<WFPlanetChunkComponent>(gridUid, out var chunkComp))
            return false;

        if (!TryComp<MapGridComponent>(gridUid, out var mapGrid))
            return false;

        chunk = (gridUid, chunkComp);
        grid = (gridUid, mapGrid);
        return true;
    }

    /// <summary>The first deep vein anchored in one tile's snap cell, if there is one.</summary>
    public bool TryGetVeinAt(Entity<MapGridComponent> grid, Vector2i idx, out Entity<WFDeepVeinComponent> vein)
    {
        vein = default;

        // Snap-cell lookup, not an area query: the vein has no fixture.
        var enumerator = _map.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, idx);

        while (enumerator.MoveNext(out var other))
        {
            if (!TryComp<WFDeepVeinComponent>(other, out var comp))
                continue;

            vein = (other.Value, comp);
            return true;
        }

        return false;
    }

    /// <summary>The chunk and the vein under an entity's own tile; the whole of the miner's placement rule.</summary>
    public bool TryGetVein(
        TransformComponent xform,
        out Entity<WFPlanetChunkComponent> chunk,
        out Entity<WFDeepVeinComponent> vein)
    {
        vein = default;

        if (!TryGetChunk(xform, out chunk, out var grid))
            return false;

        var idx = _map.TileIndicesFor(grid.Owner, grid.Comp, xform.Coordinates);

        return TryGetVeinAt(grid, idx, out vein);
    }

    /// <summary>Refuses a wrench-down anywhere but a chunk tile with a seam under it.</summary>
    private void OnAnchorAttempt(Entity<WFCrackMinerComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var xform = Transform(ent.Owner);

        // AnchorAttemptEvent carries no reason field, so each popup has to come before its own cancel.
        if (!TryGetChunk(xform, out _, out var grid))
        {
            _popup.PopupEntity(Loc.GetString("wf-crack-miner-not-chunk"), ent.Owner, args.User);
            args.Cancel();
            return;
        }

        var idx = _map.TileIndicesFor(grid.Owner, grid.Comp, xform.Coordinates);

        // No "occupied" check: AnchorableSystem already refuses an occupied tile.
        if (!TryGetVeinAt(grid, idx, out _))
        {
            _popup.PopupEntity(Loc.GetString("wf-crack-miner-no-vein"), ent.Owner, args.User);
            args.Cancel();
        }
    }

    /// <summary>Anchor and unanchor alike: flush what was cut and re-read the state. It never unanchors anything.</summary>
    private void OnAnchorStateChanged(Entity<WFCrackMinerComponent> ent, ref AnchorStateChangedEvent args)
    {
        // Deleting: the buffer is lost. A grid delete unanchors with Detaching false, hence the termination check too.
        if (args.Detaching || TerminatingOrDeleted(ent.Owner))
            return;

        if (args.Anchored)
            return;

        FlushOutput(ent, Transform(ent.Owner));

        if (ent.Comp.State != WFCrackMinerState.Broken)
            SetState(ent, WFCrackMinerState.Idle);
    }
}
