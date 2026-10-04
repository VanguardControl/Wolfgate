using Content.Shared._WF.Encounters;
using Robust.Shared.Timing;

namespace Content.Client._WF.Encounters;

/// <summary>Keeps the sector's encounter markers for the radar to draw.</summary>
public sealed partial class WFEncounterMarkerClientSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public List<WFEncounterMarker> Markers { get; private set; } = new();

    private TimeSpan _received;

    /// <summary>The zone names, looked up once since the radar draws them every frame.</summary>
    public string WarnLabel { get; private set; } = string.Empty;
    public string AttackLabel { get; private set; } = string.Empty;

    /// <summary>Seconds since the markers arrived, to move them on by their velocity.</summary>
    public float Elapsed => (float) (_timing.RealTime - _received).TotalSeconds;

    public override void Initialize()
    {
        base.Initialize();
        WarnLabel = Loc.GetString("wf-encounter-zone-warn-label");
        AttackLabel = Loc.GetString("wf-encounter-zone-attack-label");
        SubscribeNetworkEvent<WFEncounterMarkersEvent>(ev =>
        {
            Markers = ev.Markers;
            _received = _timing.RealTime;
        });
    }
}
