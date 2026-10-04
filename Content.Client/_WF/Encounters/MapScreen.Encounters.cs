using Content.Client._WF.Encounters;
using Content.Shared.Shuttles.UI.MapObjects;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    /// <summary>Adds the sector's visible encounters to a map's objects, where they were at the ping.</summary>
    private void WfAddEncounters(MapId mapId, EntityUid mapUid)
    {
        var markers = _entManager.System<WFEncounterMarkerClientSystem>();
        var map = _entManager.GetNetEntity(mapUid);
        foreach (var marker in markers.Markers)
        {
            if (marker.Map != mapId)
                continue;

            var position = marker.Position + marker.Velocity * markers.Elapsed;
            var name = Loc.GetString("wf-encounter-map-object", ("name", marker.Name));
            _pendingMapObjects.Add((mapId, new ShuttleBeaconObject(NetEntity.Invalid, new NetCoordinates(map, position), name)));
        }
    }
}
