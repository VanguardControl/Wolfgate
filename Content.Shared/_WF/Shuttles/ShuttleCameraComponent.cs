using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Shuttles;

/// <summary>
/// Which hull camera a pilot is looking through.
/// </summary>
[Serializable, NetSerializable]
public enum ShuttleCameraView : byte
{
    /// <summary>The pilot's own eyes, at the console.</summary>
    Helm,
    Front,
    Rear,
    Left,
    Right,
}

/// <summary>
/// Sits on a pilot while they fly, holding the view their console is feeding them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShuttleCameraComponent : Component
{
    public const float MinZoom = 1f;
    public const float MaxZoom = 4f;
    public const float ZoomStep = 0.25f;

    [DataField, AutoNetworkedField]
    public ShuttleCameraView View = ShuttleCameraView.Helm;

    [DataField, AutoNetworkedField]
    public float Zoom = 1.5f;

    /// <summary>
    /// Whether hull views are fed through a low-light camera, which the client draws. The helm never
    /// is, those are the pilot's own eyes.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool LowLight;

    /// <summary>
    /// The hull camera the eye rides on, or null at the helm.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Camera;

    public override bool SendOnlyToOwner => true;
}

/// <summary>
/// Remembers the last view picked at a console, so sitting back down doesn't reset it.
/// </summary>
[RegisterComponent]
public sealed partial class ShuttleCameraSettingsComponent : Component
{
    [DataField]
    public ShuttleCameraView View = ShuttleCameraView.Helm;

    [DataField]
    public float? Zoom;

    [DataField]
    public bool LowLight;
}

/// <summary>Pilot picked a camera view, zoom or low-light setting.</summary>
[Serializable, NetSerializable]
public sealed class ShuttleCameraSetMessage : BoundUserInterfaceMessage
{
    public ShuttleCameraView View;
    public float Zoom;
    public bool LowLight;

    public ShuttleCameraSetMessage(ShuttleCameraView view, float zoom, bool lowLight)
    {
        View = view;
        Zoom = zoom;
        LowLight = lowLight;
    }
}
