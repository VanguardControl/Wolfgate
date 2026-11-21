using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WG.ColdStorage;

/// <summary>Raised on every system when pressure has moved by 0.05 or more since the last raise, and when it returns to 0.</summary>
[ByRefEvent]
public readonly record struct WGEntityPressureChangedEvent(float Pressure);

/// <summary>
/// Turns the entity count into a pressure from 0 to 1 that other systems scale their budgets by: the busier the
/// server, the less it keeps and the less it spawns. Smoothed so one mass spawn or delete doesn't whip the knobs.
/// </summary>
public sealed class WGEntityPressureSystem : EntitySystem
{
    /// <summary>Largest move toward the target in one sample, which is one second.</summary>
    public const float MaxStep = 0.02f;

    /// <summary>Move since the last raise that is worth telling everyone about.</summary>
    public const float RaiseStep = 0.05f;

    private static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(1);

    private static readonly Gauge PressureGauge = Metrics.CreateGauge(
        "wg_entity_pressure",
        "Entity pressure from 0 (relaxed) to 1 (strict), from the entity count between wg.pressure.low and high");

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    private bool _enabled;
    private int _low;
    private int _high;
    private TimeSpan _next;
    private float _raised;
    private int _level;

    /// <summary>How many times the changed event has been raised, for tests.</summary>
    internal int Raised;

    /// <summary>Current pressure: 0 relaxed, 1 strict.</summary>
    public float Pressure { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, WGPressureCVars.Enabled, value => _enabled = value, true);
        Subs.CVar(_cfg, WGPressureCVars.Low, value => _low = value, true);
        Subs.CVar(_cfg, WGPressureCVars.High, value => _high = value, true);
    }

    /// <summary><paramref name="relaxed"/> at pressure 0, <paramref name="strict"/> at pressure 1, linear between.</summary>
    public float Scale(float relaxed, float strict)
    {
        return relaxed + (strict - relaxed) * Pressure;
    }

    /// <summary>The pressure an entity count maps to, before smoothing.</summary>
    public float Target(int count)
    {
        if (!_enabled)
            return 0f;

        var span = Math.Max(_high - _low, 1);
        return Math.Clamp((count - _low) / (float) span, 0f, 1f);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + SampleEvery;
        SampleNow(false);
    }

    /// <summary>Samples the entity count now; <paramref name="snap"/> jumps to the target instead of stepping toward it.</summary>
    public void SampleNow(bool snap)
    {
        var count = EntityManager.EntityCount;
        var target = Target(count);
        var previous = Pressure;

        if (!_enabled || snap)
            Pressure = target;
        else
            Pressure += Math.Clamp(target - Pressure, -MaxStep, MaxStep);

        PressureGauge.Set(Pressure);
        LogLevel(count);

        if (MathF.Abs(Pressure - _raised) >= RaiseStep || Pressure == 0f && _raised != 0f)
        {
            _raised = Pressure;
            Raised++;
            var ev = new WGEntityPressureChangedEvent(Pressure);
            RaiseLocalEvent(ref ev);
        }
    }

    private void LogLevel(int count)
    {
        // 0 below 0.25, then 1, 2, 3 and 4 at full
        var level = (int) MathF.Floor(Pressure / 0.25f);
        if (level == _level)
            return;

        var direction = level > _level ? "up" : "down";
        _level = level;
        Log.Info($"Entity pressure {direction} to {Pressure:F2} at {count} entities (level {level} of 4).");
    }
}
