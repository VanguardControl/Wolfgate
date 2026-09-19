using System;
using System.Text.Json.Nodes;
using System.Threading;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Content.Shared.Symphony;
using Robust.Server.ServerStatus;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Server.Symphony;

/// <summary>
/// Reports the Symphony hook version in /status, so the SSymphony panel can check the build it is talking to, the
/// test merges this build carries, so the panel can say what is actually running, and the round clock as the game
/// keeps it. A system of its own rather than a line in the game ticker's status shell, so upstream's file stays
/// untouched.
/// </summary>
public sealed partial class SymphonyStatusSystem : EntitySystem
{
    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;

    // The round duration in ticks as of the last game tick, or -1 outside a round. Written on the main thread, read by
    // the status host's, hence Interlocked. The server stops ticking while it is paused with nobody on, and the
    // game's round clock stops with it, so a value that stops updating then is the right one.
    private long _roundTicks = -1;

    public override void Initialize()
    {
        base.Initialize();
        _statusHost.OnStatusRequest += OnStatusRequest;
        SubscribeLocalEvent<GameRunLevelChangedEvent>(_ => Sample());
    }

    public override void Shutdown()
    {
        _statusHost.OnStatusRequest -= OnStatusRequest;
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        Sample();
    }

    /// <summary>
    /// The same duration the game shows players: current game time since the round started, which leaves out any
    /// time the server spent paused. Nothing in the lobby.
    /// </summary>
    private void Sample()
    {
        var ticks = _ticker.RunLevel == GameRunLevel.PreRoundLobby ? -1 : Math.Max(0, _ticker.RoundDuration().Ticks);
        Interlocked.Exchange(ref _roundTicks, ticks);
    }

    // Raised off the main thread. Constants, a list fixed at startup and the sampled clock are what is safe to read.
    private void OnStatusRequest(JsonNode json)
    {
        json["symphony_module"] = SharedSymphony.ModuleVersion;
        json["symphony_test_merges"] = SymphonyTestMerges.StatusJson();
        var ticks = Interlocked.Read(ref _roundTicks);
        json["symphony_round_duration"] = ticks < 0 ? -1 : TimeSpan.FromTicks(ticks).TotalSeconds;
        json["symphony_paused"] = _timing.Paused;
    }
}
