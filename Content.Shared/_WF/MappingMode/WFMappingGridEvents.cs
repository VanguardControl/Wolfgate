using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode;

/// <summary>
/// One grid on the mapper's level: its name, tiles and entities on it, contents included, and whether it sits on whole
/// tiles at a right angle, so it lines up with grids on other levels.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingGridInfo(NetEntity Grid, string Name, int Tiles, int Entities, bool Aligned = true);

/// <summary>Asks for the grids on the level the mapper is on.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingGridListRequest : EntityEventArgs;

/// <summary>Server to client: the grids on the mapper's level, largest first.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingGridListEvent(List<WFMappingGridInfo> grids) : EntityEventArgs
{
    public readonly List<WFMappingGridInfo> Grids = grids;
}

/// <summary>Moves the mapper to the centre of a grid on one of their session's maps.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingGoToGridRequest(NetEntity grid) : EntityEventArgs
{
    public readonly NetEntity Grid = grid;
}

/// <summary>Moves a grid on one of the mapper's session maps onto whole tiles and the nearest right angle.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingSnapGridRequest(NetEntity grid) : EntityEventArgs
{
    public readonly NetEntity Grid = grid;
}

/// <summary>Deletes a whole grid on one of the mapper's session maps, as one undoable edit.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingDeleteGridRequest(NetEntity grid) : EntityEventArgs
{
    public readonly NetEntity Grid = grid;
}
