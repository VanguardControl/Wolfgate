using Content.Client.Decals;
using Robust.Shared.Network;
using Content.Client.Decals.Overlays;

namespace Content.Client._WF.MappingMode;

/// <summary>A view layer the mapping screen can show or hide on this client.</summary>
public enum WFMappingLayer : byte
{
    Walls,
    Subfloor,
    Decals,
    Markers,
    Lights,
}

public sealed partial class WFMappingClientSystem
{
    [Dependency] private DecalSystem _decalSystem = default!;
    [Dependency] private IClientNetManager _netManager = default!;

    public static readonly WFMappingLayer[] Layers =
    {
        WFMappingLayer.Walls,
        WFMappingLayer.Subfloor,
        WFMappingLayer.Decals,
        WFMappingLayer.Markers,
        WFMappingLayer.Lights,
    };

    /// <summary>Whether a layer is shown.</summary>
    public bool GetLayer(WFMappingLayer layer)
    {
        return layer switch
        {
            WFMappingLayer.Walls => !_tools.WallsHidden,
            WFMappingLayer.Subfloor => _subFloor.ShowAll,
            WFMappingLayer.Decals => _overlays.HasOverlay<DecalOverlay>(),
            WFMappingLayer.Markers => _markers.MarkersVisible,
            _ => _light.Enabled,
        };
    }

    /// <summary>Shows or hides a layer.</summary>
    public void SetLayer(WFMappingLayer layer, bool shown)
    {
        switch (layer)
        {
            case WFMappingLayer.Walls:
                _tools.SetWallsHidden(!shown);
                break;
            case WFMappingLayer.Subfloor:
                // The setter asks the server too, which logs an error once the client has disconnected.
                if (_subFloor.ShowAll != shown && _netManager.IsConnected)
                    _subFloor.ShowAll = shown;
                break;
            case WFMappingLayer.Decals:
                if (GetLayer(WFMappingLayer.Decals) != shown)
                    _decalSystem.ToggleOverlay();
                break;
            case WFMappingLayer.Markers:
                _markers.MarkersVisible = shown;
                break;
            case WFMappingLayer.Lights:
                _light.Enabled = shown;
                break;
        }
    }

    /// <summary>
    /// Puts every layer back: the mapping defaults (markers, subfloor, no lighting) or the normal game's.
    /// </summary>
    private void ResetLayers(bool mapping)
    {
        SetLayer(WFMappingLayer.Walls, true);
        SetLayer(WFMappingLayer.Decals, true);
        SetLayer(WFMappingLayer.Subfloor, mapping);
        SetLayer(WFMappingLayer.Markers, mapping);
        SetLayer(WFMappingLayer.Lights, !mapping);
    }
}
