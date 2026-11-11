using System.Diagnostics;
using Content.Shared._WF.DeviceNetGuard;
using Robust.Shared.Configuration;

namespace Content.Server.DeviceNetwork.Systems;

public sealed partial class DeviceNetworkSystem
{
    [Dependency] private IConfigurationManager _guardCfg = default!;

    private readonly Stopwatch _guardWatch = new();

    /// <summary>Packets waiting to be sent, in this tick's queue and the next.</summary>
    public int Backlog => _queueA.Count + _queueB.Count;

    /// <summary>Packets dropped by the guard since the server started.</summary>
    public long ShedPackets { get; private set; }

    /// <summary>
    ///     Sends this tick's packets within a time budget and drops a runaway backlog. Returns false when the
    ///     guard is off and the upstream loop should run.
    /// </summary>
    private bool GuardedUpdate()
    {
        if (!_guardCfg.GetCVar(DeviceNetGuardCVars.Enabled))
            return false;

        var backlog = Backlog;
        if (backlog > _guardCfg.GetCVar(DeviceNetGuardCVars.MaxBacklog))
        {
            // Devices feeding each other more packets than they consume never settle; start them from nothing.
            ShedPackets += backlog;
            _queueA.Clear();
            _queueB.Clear();
            Log.Error($"Device network packet storm: dropped {backlog} queued packets.");
            return true;
        }

        var budget = TimeSpan.FromMilliseconds(_guardCfg.GetCVar(DeviceNetGuardCVars.TickBudgetMs));
        var minPackets = _guardCfg.GetCVar(DeviceNetGuardCVars.MinPacketsPerTick);
        var sent = 0;
        _guardWatch.Restart();

        while (_activeQueue.Count > 0)
        {
            // The rest stays at the head of the active queue; newer packets keep collecting behind it.
            if (sent >= minPackets && _guardWatch.Elapsed >= budget)
                return true;

            SendPacket(_activeQueue.Dequeue());
            sent++;
        }

        SwapQueues();
        return true;
    }
}
