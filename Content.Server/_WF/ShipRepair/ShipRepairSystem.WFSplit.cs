using Content.Shared._Mono.ShipRepair.Components;
using Robust.Server.Physics;

namespace Content.Server._Mono.ShipRepair;

/// <summary>
/// A hull that breaks up - a planetary crash, a seam cut by a blast - splits into sections that keep the parent's tile
/// indices and pose. Each section gets a copy of the parent's repair snapshot, so an SRD can rebuild the ship from
/// whichever piece the crew is standing on.
/// </summary>
public sealed partial class ShipRepairSystem
{
    private void InitializeSplit()
    {
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
    }

    private void OnGridSplit(ref GridSplitEvent args)
    {
        if (!TryComp<ShipRepairDataComponent>(args.Grid, out var parent))
            return;

        foreach (var section in args.NewGrids)
        {
            if (TerminatingOrDeleted(section) || HasComp<ShipRepairDataComponent>(section))
                continue;

            var copy = EnsureComp<ShipRepairDataComponent>(section);
            copy.ChunkSize = parent.ChunkSize;
            copy.EntityPalette = new List<Robust.Shared.Prototypes.EntProtoId>(parent.EntityPalette);

            foreach (var (index, chunk) in parent.Chunks)
            {
                var chunkCopy = new ShipRepairChunk
                {
                    Tiles = (int[]) chunk.Tiles.Clone(),
                    NextUid = chunk.NextUid,
                };

                foreach (var (id, spec) in chunk.Entities)
                {
                    chunkCopy.Entities[id] = new ShipRepairEntitySpecifier
                    {
                        ProtoIndex = spec.ProtoIndex,
                        LocalPosition = spec.LocalPosition,
                        Rotation = spec.Rotation,
                        OriginalEntity = spec.OriginalEntity,
                    };
                }

                copy.Chunks[index] = chunkCopy;
            }

            Dirty(section, copy);
        }
    }
}
