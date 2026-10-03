using Content.Client.Administration.Managers;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Administration;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._WF.NpcCrew;

/// <summary>Exchanges crew setup requests with the permission-checked server endpoint.</summary>
public sealed partial class WFCrewSetupClientSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IClientAdminManager _admin = default!;

    /// <summary>The live crews from the last reply, drawn on an admin's radar.</summary>
    public List<WFCrewSetupCrew> Crews { get; private set; } = new();
    private TimeSpan _radarUntil;
    public WFCrewSetupResponse? Preview { get; private set; }
    public TimeSpan PreviewUntil { get; private set; }
    public event Action<WFCrewSetupResponse>? Received;
    private float _pollTimer;
    private int _nextRequestId;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var radar = _timing.RealTime < _radarUntil && _admin.HasFlag(AdminFlags.Spawn);
        if (Received == null && !radar)
        {
            _pollTimer = 0;
            Crews.Clear();
            return;
        }
        _pollTimer += frameTime;
        if (_pollTimer < 2)
            return;
        _pollTimer = 0;
        Send(new WFCrewSetupRequest { Action = WFCrewSetupAction.List });
    }

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WFCrewSetupOverlay(this, EntityManager, _timing));
        SubscribeNetworkEvent<WFCrewSetupOpenEvent>(ev =>
        {
            var window = new WFCrewSetupWindow();
            window.SelectCrew(ev.Grid, ev.Group);
            window.OpenCentered();
        });
        SubscribeNetworkEvent<WFCrewSetupResponse>(response =>
        {
            if (response.Action == WFCrewSetupAction.Preview)
            {
                Preview = response;
                PreviewUntil = _timing.CurTime + TimeSpan.FromSeconds(30);
            }
            Crews = response.Crews;
            Received?.Invoke(response);
        });
    }

    /// <summary>Keeps the crew list fresh while a radar is drawing it.</summary>
    public void WatchRadar()
    {
        _radarUntil = _timing.RealTime + TimeSpan.FromSeconds(3);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WFCrewSetupOverlay>();
        base.Shutdown();
    }

    /// <summary>Sends an operation with a unique identifier for routing its eventual response.</summary>
    public int Send(WFCrewSetupRequest request)
    {
        request.RequestId = ++_nextRequestId;
        EntityManager.System<WFCrewUiDiagnosticsSystem>().Request("crew", request.Grid);
        RaiseNetworkEvent(request);
        return request.RequestId;
    }
}
