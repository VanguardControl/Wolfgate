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

    /// <summary>Entities, decals and tiles together.</summary>
    Everything,
}

/// <summary>What Shape mode draws.</summary>
public enum WFMappingShapeKind : byte
{
    Line,
    Rect,

    /// <summary>The ellipse inscribed in the dragged box.</summary>
    Circle,
}

/// <summary>Whether a rectangle or circle is drawn whole or only its border.</summary>
public enum WFMappingShapeFill : byte
{
    Fill,
    Border,
}
