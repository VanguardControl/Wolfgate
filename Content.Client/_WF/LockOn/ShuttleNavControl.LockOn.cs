using System.Numerics;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>Expresses a radar click in the plotted map, for a lock request.</summary>
    public EntityCoordinates WfLockOnCoordinates(Vector2 relativePosition) => GetMouseEntityCoordinates(relativePosition);

    /// <summary>Whether the radar's primary button is held, so only the release of a click locks.</summary>
    public bool WfLockOnMouseDown() => _isMouseDown;

    /// <summary>The radar's current world-to-pixel transform and the map it plots, for the lock overlay.</summary>
    public bool WfLockOnTryGetWorldToView(out Matrix3x2 worldToView, out MapId map)
    {
        worldToView = default;
        map = MapId.Nullspace;
        if (_coordinates == null || _rotation == null ||
            !EntManager.TryGetComponent<TransformComponent>(_coordinates.Value.EntityId, out var xform) ||
            xform.MapID == MapId.Nullspace)
            return false;

        var centre = _transform.ToMapCoordinates(_coordinates.Value).Offset(_rotation.Value.RotateVec(Offset));
        var worldToShuttle = Matrix3Helpers.CreateTranslation(-centre.Position) * Matrix3Helpers.CreateRotation(-_rotation.Value);
        var shuttleToView = Matrix3x2.CreateScale(new Vector2(MinimapScale, -MinimapScale)) * Matrix3x2.CreateTranslation(MidPointVector);
        worldToView = worldToShuttle * shuttleToView;
        map = xform.MapID;
        return true;
    }
}
