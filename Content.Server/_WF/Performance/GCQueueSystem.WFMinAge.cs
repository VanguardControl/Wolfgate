using Content.Server.Worldgen.Prototypes;
using Content.Shared.GameTicking;
using Robust.Shared.Timing;

namespace Content.Server.Worldgen.Systems.GC;

public sealed partial class GCQueueSystem
{
    [Dependency] private IGameTiming _wfTiming = default!;

    /// <summary>When each queued entity joined its queue, for queues with a minimum age.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _wfQueuedAt = new();

    private void WFInitializeMinAge()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _wfQueuedAt.Clear());
    }

    private void WFOnQueued(EntityUid uid, GCQueuePrototype proto)
    {
        if (proto.WFMinAge > TimeSpan.Zero)
            _wfQueuedAt[uid] = _wfTiming.CurTime;
    }

    /// <summary>
    /// Whether the queue's oldest entity is too young to delete yet. Queues are first in, first out, so everything
    /// behind it is younger still.
    /// </summary>
    private bool WFTooYoung(Queue<EntityUid> queue, GCQueuePrototype proto)
    {
        return proto.WFMinAge > TimeSpan.Zero &&
               queue.TryPeek(out var oldest) &&
               _wfQueuedAt.TryGetValue(oldest, out var at) &&
               _wfTiming.CurTime - at < proto.WFMinAge;
    }

    private void WFOnDequeued(EntityUid uid)
    {
        _wfQueuedAt.Remove(uid);
    }
}
