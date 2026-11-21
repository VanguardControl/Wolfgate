using Robust.Shared.Configuration;

namespace Content.Shared._WG.JoinQueue;

/// <summary>Settings for the queue players wait in when the server is at its soft player cap.</summary>
[CVarDefs]
public sealed class JoinQueueCVars
{
    /// <summary>Whether a full server queues players. Off, it refuses them as before.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.join_queue.enabled", true, CVar.SERVERONLY);

    /// <summary>Most players the queue holds; past it the server refuses the connection.</summary>
    public static readonly CVarDef<int> MaxSize =
        CVarDef.Create("wg.join_queue.max_size", 50, CVar.SERVERONLY);

    /// <summary>Seconds a queued player who disconnects keeps their place.</summary>
    public static readonly CVarDef<float> HoldSeconds =
        CVarDef.Create("wg.join_queue.hold_seconds", 120f, CVar.SERVERONLY);
}
