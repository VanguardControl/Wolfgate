using Content.Client._WF.CustomMarkings;
using Content.Shared._WF.AppearanceStudio;

namespace Content.Client._WF.AppearanceStudio;

/// <summary>Sends painted warps to the server to store, and keeps the stored art at hand once it answers.</summary>
public sealed class StudioWarpSystem : EntitySystem
{
    [Dependency] private CustomMarkingSystem _customMarkings = default!;

    private readonly Dictionary<int, StudioWarp> _pending = new();
    private int _nextRequest;

    /// <summary>Raised when the server answers a save.</summary>
    public event Action<StudioWarpSaveResultEvent>? SaveAnswered;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<StudioWarpSaveResultEvent>(OnSaveResult);
    }

    /// <summary>Asks the server to store a warp. Returns the request number its answer carries.</summary>
    public int Save(StudioWarp warp)
    {
        var request = ++_nextRequest;
        _pending[request] = warp.Clone();
        RaiseNetworkEvent(new StudioWarpSaveEvent(request, warp.ToArt().Pixels));
        return request;
    }

    private void OnSaveResult(StudioWarpSaveResultEvent ev)
    {
        // The art is known here already; keeping it saves a trip to fetch it back.
        if (_pending.Remove(ev.Request, out var warp) && ev.Hash != null)
            _customMarkings.Remember(ev.Hash, warp.ToArt());

        SaveAnswered?.Invoke(ev);
    }
}
