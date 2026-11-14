namespace Content.Shared._WF.Construction;

/// <summary>
/// A wall fixture that sits on the floor tile it faces, such as a light: anchoring a machine on that tile ignores its
/// fixture, which otherwise blocked wrenching down anything solid under a wall light.
/// </summary>
[RegisterComponent]
public sealed partial class WFAnchorPassableComponent : Component;
