using Content.Shared._WF.Encounters;
using Robust.Shared.Timing;

namespace Content.Client._WF.Encounters;

/// <summary>Keeps the sector's encounter markers for the radar to draw.</summary>
public sealed partial class WFEncounterMarkerClientSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public List<WFEncounterMarker> Markers { get; private set; } = new();

    private TimeSpan _received;

    /// <summary>Seconds since the markers arrived, to move them on by their velocity.</summary>
    public float Elapsed => (float) (_timing.RealTime - _received).TotalSeconds;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFEncounterMarkersEvent>(ev =>
        {
            Markers = ev.Markers;
            _received = _timing.RealTime;
        });
    }
}
