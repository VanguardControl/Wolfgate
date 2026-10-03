using Content.Shared._WF.NpcCrew;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._WF.NpcCrew;

/// <summary>Exchanges crew setup requests with the permission-checked server endpoint.</summary>
public sealed partial class WFCrewSetupClientSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;
    public WFCrewSetupResponse? Preview { get; private set; }
    public TimeSpan PreviewUntil { get; private set; }
    public event Action<WFCrewSetupResponse>? Received;
    private float _pollTimer;
    private int _nextRequestId;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (Received == null)
        {
            _pollTimer = 0;
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
            Received?.Invoke(response);
        });
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
        RaiseNetworkEvent(request);
        return request.RequestId;
    }
}
