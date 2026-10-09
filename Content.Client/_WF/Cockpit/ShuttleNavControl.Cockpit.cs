using System.Numerics;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>Uses the radar's existing pan, zoom and rotation transform for cockpit aiming.</summary>
    public EntityCoordinates WfCockpitAimCoordinates(Vector2 relativePosition) => GetMouseEntityCoordinates(relativePosition);

    /// <summary>Keeps the radar's tracking frame consistent with middle-mouse panning.</summary>
    protected override void WfCockpitPanMoved()
    {
        if (_coordinates == null)
            return;

        if (!_relativePanning &&
            _coordinates?.EntityId is { } coordEnt &&
            EntManager.TryGetComponent<TransformComponent>(coordEnt, out var coordXform) &&
            coordXform.MapUid != coordEnt)
            _coordinates = _transform.ToCoordinates(_transform.ToMapCoordinates(_coordinates.Value));

        _wasPanned = true;
    }
}
