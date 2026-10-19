namespace Content.Client._WF.MappingMode.Modes;

/// <summary>The mapping screen's build modes, in the order of their number keys.</summary>
public enum WFMappingBuildMode : byte
{
    Place = 1,
    Erase,
    Select,
    Fill,
    Shape,
    Pick,
    Template,
}

/// <summary>What Erase mode removes.</summary>
public enum WFMappingEraseTarget : byte
{
    Entity,
    Tile,
    Decal,
}

/// <summary>What Shape mode draws.</summary>
public enum WFMappingShapeKind : byte
{
    Line,
    FilledRect,
    HollowRect,
}
