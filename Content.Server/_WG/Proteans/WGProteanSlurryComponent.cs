namespace Content.Server._WG.Proteans;

/// <summary>Nanite slurry on the floor: mass a Protean shed when hurt, which any Protean takes back by walking over it.</summary>
[RegisterComponent]
public sealed partial class WGProteanSlurryComponent : Component
{
    /// <summary>The nanomass it carries.</summary>
    [DataField]
    public float Mass;
}
