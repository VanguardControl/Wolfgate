using System.Linq;
using System.Numerics;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.AsteroidMining;

/// <summary>Mining beam console: pick a mining laser and a rock, read its scan, and steer the laser's power into the window.</summary>
public sealed class WFMiningBeamConsoleWindow : DefaultWindow
{
    public event Action<NetEntity, float>? OnPower;

    private static readonly float[] PowerSteps = { -0.1f, -0.05f, -0.01f, 0.01f, 0.05f, 0.1f };

    private readonly WFMiningBeamRadarControl _radar;
    private readonly OptionButton _emitters;
    private readonly OptionButton _targets;
    private readonly Label _connection;
    private readonly Label _status;
    private readonly Label _scan;
    private readonly BoxContainer _ores;
    private readonly Label _stability;
    private readonly WFMiningChargeMeter _meter;
    private readonly Label _meterLabel;
    private readonly ProgressBar _progress;
    private readonly Label _overcharge;
    private readonly Label _power;
    private WFMiningBeamConsoleBoundUserInterfaceState? _state;
    private NetEntity? _selectedEmitter;
    private NetEntity? _selectedTarget;
    private float? _pendingPower;

    public WFMiningBeamConsoleWindow()
    {
        Title = Loc.GetString("wf-mining-beam-console-title");
        SetSize = new Vector2(860, 520);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        Contents.AddChild(root);
        root.AddChild(_connection = new Label());
        var body = new BoxContainer { HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 12 };
        root.AddChild(body);
        body.AddChild(_radar = new WFMiningBeamRadarControl());
        var controls = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = 360,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        body.AddChild(controls);

        var emitterRow = new BoxContainer { SeparationOverride = 8 };
        controls.AddChild(emitterRow);
        emitterRow.AddChild(new Label { Text = Loc.GetString("wf-mining-beam-console-emitter"), MinWidth = 90 });
        emitterRow.AddChild(_emitters = new OptionButton { HorizontalExpand = true, MaxWidth = 300, RectClipContent = true });
        var targetRow = new BoxContainer { SeparationOverride = 8 };
        controls.AddChild(targetRow);
        targetRow.AddChild(new Label { Text = Loc.GetString("wf-mining-beam-console-target"), MinWidth = 90 });
        targetRow.AddChild(_targets = new OptionButton { HorizontalExpand = true, MaxWidth = 300, RectClipContent = true });
        controls.AddChild(_status = new Label { ClipText = true });

        controls.AddChild(new Label
        {
            Text = Loc.GetString("wf-mining-beam-console-gunnery-hint"),
            FontColorOverride = Color.Gray,
        });

        var powerRow = new BoxContainer { SeparationOverride = 4 };
        controls.AddChild(powerRow);
        powerRow.AddChild(_power = new Label { MinWidth = 110 });
        foreach (var step in PowerSteps)
        {
            var button = new Button
            {
                Text = Loc.GetString(step > 0 ? "wf-mining-beam-console-power-up" : "wf-mining-beam-console-power-down",
                    ("step", (int) MathF.Round(MathF.Abs(step) * 100))),
                HorizontalExpand = true,
            };
            button.OnPressed += _ => StepPower(step);
            powerRow.AddChild(button);
        }

        controls.AddChild(_meterLabel = new Label());
        controls.AddChild(_meter = new WFMiningChargeMeter { ToolTip = Loc.GetString("wf-mining-beam-console-meter-help") });
        controls.AddChild(new Label { Text = Loc.GetString("wf-mining-beam-console-progress") });
        controls.AddChild(_progress = new ProgressBar { MinValue = 0, MaxValue = 1, MinHeight = 14 });
        controls.AddChild(_overcharge = new Label
        {
            Text = Loc.GetString("wf-mining-beam-console-overcharge"),
            FontColorOverride = Color.Red,
            Visible = false,
        });

        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        controls.AddChild(scroll);
        var scan = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, HorizontalExpand = true };
        scroll.AddChild(scan);
        scan.AddChild(_scan = new Label());
        scan.AddChild(_ores = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical });
        scan.AddChild(_stability = new Label());

        _emitters.OnItemSelected += args =>
        {
            if (_state == null || args.Id < 0 || args.Id >= _state.Emitters.Length)
                return;
            _selectedEmitter = _state.Emitters[args.Id].Entity;
            _pendingPower = null;
            _emitters.SelectId(args.Id);
            if (SelectedEmitter() is { Target: { } target })
                _selectedTarget = target;
            Refresh();
        };
        _targets.OnItemSelected += args =>
        {
            if (_state == null || args.Id < 0 || args.Id > _state.Targets.Length)
                return;
            _selectedTarget = args.Id == 0 ? null : _state.Targets[args.Id - 1].Entity;
            _targets.SelectId(args.Id);
            Refresh();
        };
        _radar.OnTargetSelected += entity =>
        {
            _selectedTarget = entity;
            if (_state != null)
                _targets.SelectId(Array.FindIndex(_state.Targets, t => t.Entity == entity) + 1);
            Refresh();
        };
    }

    private WFMiningBeamEmitterEntry? SelectedEmitter()
    {
        foreach (var entry in _state?.Emitters ?? Array.Empty<WFMiningBeamEmitterEntry>())
        {
            if (entry.Entity == _selectedEmitter)
                return entry;
        }

        return null;
    }

    private WFMiningBeamTargetEntry? SelectedTarget()
    {
        foreach (var entry in _state?.Targets ?? Array.Empty<WFMiningBeamTargetEntry>())
        {
            if (entry.Entity == _selectedTarget)
                return entry;
        }

        return null;
    }

    private void StepPower(float step)
    {
        if (SelectedEmitter() is not { } emitter)
            return;

        var power = MathF.Round(Math.Clamp((_pendingPower ?? emitter.Power) + step, 0f, 1f), 2);
        _pendingPower = power;
        OnPower?.Invoke(emitter.Entity, power);
        Refresh();
    }

    public void UpdateState(WFMiningBeamConsoleBoundUserInterfaceState state)
    {
        // Rebuild the dropdowns only when their entries change, so an open list survives routine updates.
        var emittersChanged = _state == null || !_state.Emitters.Select(e => e.Entity).SequenceEqual(state.Emitters.Select(e => e.Entity));
        var targetsChanged = _state == null || !_state.Targets.Select(t => t.Entity).SequenceEqual(state.Targets.Select(t => t.Entity));
        _state = state;
        _connection.Text = Loc.GetString(state.Connected
            ? "wf-mining-beam-console-connected"
            : "wf-mining-beam-console-disconnected");

        if (_selectedEmitter == null || state.Emitters.All(e => e.Entity != _selectedEmitter))
        {
            _selectedEmitter = state.Emitters.Length > 0 ? state.Emitters[0].Entity : null;
            _pendingPower = null;
        }

        if (SelectedEmitter() is { } emitter)
        {
            if (_pendingPower is { } pending && MathF.Abs(pending - emitter.Power) < 0.005f)
                _pendingPower = null;
            if (emitter.Active && emitter.Target is { } firing)
                _selectedTarget ??= firing;
        }

        if (_selectedTarget != null && state.Targets.All(t => t.Entity != _selectedTarget))
            _selectedTarget = null;

        if (emittersChanged)
        {
            _emitters.Clear();
            for (var i = 0; i < state.Emitters.Length; i++)
                _emitters.AddItem($"{i + 1}. {state.Emitters[i].Name}", i);
            if (state.Emitters.Length == 0)
                _emitters.AddItem(Loc.GetString("wf-mining-beam-console-no-emitters"), 0);
        }

        _emitters.Disabled = state.Emitters.Length == 0;
        _emitters.SelectId(Math.Max(0, Array.FindIndex(state.Emitters, e => e.Entity == _selectedEmitter)));

        if (targetsChanged)
        {
            _targets.Clear();
            _targets.AddItem(Loc.GetString(state.Targets.Length == 0
                ? "wf-mining-beam-console-no-targets"
                : "wf-mining-beam-console-pick-target"), 0);
            for (var i = 0; i < state.Targets.Length; i++)
            {
                var target = state.Targets[i];
                _targets.AddItem(target.Ores.Length > 0
                    ? Loc.GetString("wf-mining-beam-console-target-entry", ("name", target.Name), ("ore", target.Ores[0].Name))
                    : target.Name, i + 1);
            }
        }

        _targets.Disabled = state.Targets.Length == 0;
        _targets.SelectId(Array.FindIndex(state.Targets, t => t.Entity == _selectedTarget) + 1);
        Refresh();
    }

    private void Refresh()
    {
        var emitter = SelectedEmitter();
        var target = SelectedTarget();
        _radar.Targets = _state?.Targets ?? Array.Empty<WFMiningBeamTargetEntry>();
        _radar.Range = _state?.Range ?? 150f;
        _radar.Emitter = emitter;
        _radar.Selected = _selectedTarget;

        var power = _pendingPower ?? emitter?.Power ?? 0f;
        _power.Text = Loc.GetString("wf-mining-beam-console-power", ("power", (int) MathF.Round(power * 100)));

        var firing = emitter is { Active: true } active && target != null && active.Target == target.Value.Entity;
        _status.Text = Status(emitter, target, firing);

        if (target is not { } rock)
        {
            _scan.Text = Loc.GetString("wf-mining-beam-console-no-target");
            _ores.RemoveAllChildren();
            _stability.Text = string.Empty;
            _meterLabel.Text = string.Empty;
            _meter.Charge = 0f;
            _meter.Predicted = null;
            _progress.Value = 0;
            _overcharge.Visible = false;
            return;
        }

        _scan.Text = Loc.GetString("wf-mining-beam-console-scan", ("name", rock.Name), ("mass", (int) MathF.Round(rock.Mass)),
            ("radius", (int) MathF.Round(rock.Radius)));
        _ores.RemoveAllChildren();
        foreach (var ore in rock.Ores)
        {
            _ores.AddChild(new Label
            {
                Text = Loc.GetString("wf-mining-beam-console-ore", ("name", ore.Name), ("percent", (int) MathF.Round(ore.Fraction * 100))),
                FontColorOverride = ore.Color,
            });
        }

        var inert = 1f - rock.Ores.Sum(o => o.Fraction);
        _ores.AddChild(new Label
        {
            Text = Loc.GetString("wf-mining-beam-console-inert", ("percent", (int) MathF.Round(inert * 100))),
            FontColorOverride = Color.Gray,
        });
        _stability.Text = Loc.GetString("wf-mining-beam-console-stability",
            ("resistance", (int) MathF.Round(rock.Resistance * 100)),
            ("instability", (int) MathF.Round(rock.Instability * 100)));

        _meter.Charge = rock.Charge;
        _meter.WindowLow = rock.WindowLow;
        _meter.WindowHigh = rock.WindowHigh;
        _meter.Overcharging = rock.Overcharge > 0f;
        _meter.Predicted = Predict(emitter, rock, firing, power);
        _meterLabel.Text = Loc.GetString("wf-mining-beam-console-charge", ("charge", (int) MathF.Round(rock.Charge * 100)),
            ("low", (int) MathF.Round(rock.WindowLow * 100)), ("high", (int) MathF.Round(rock.WindowHigh * 100)));
        _progress.Value = rock.Progress;
        _overcharge.Visible = rock.Overcharge > 0f;
    }

    /// <summary>Where the rock's heat will settle with this emitter at the given power and every other beam unchanged.</summary>
    private static float? Predict(WFMiningBeamEmitterEntry? emitter, WFMiningBeamTargetEntry rock, bool firing, float power)
    {
        if (emitter is not { } dish)
            return null;

        var others = firing ? MathF.Max(0f, rock.IncomingPower - dish.Power * dish.Strength) : rock.IncomingPower;
        return (others + power * dish.Strength) * (1f - rock.Resistance);
    }

    private static string Status(WFMiningBeamEmitterEntry? emitter, WFMiningBeamTargetEntry? target, bool firing)
    {
        if (emitter is not { } dish)
            return Loc.GetString("wf-mining-beam-status-no-emitter");
        if (dish.Cooldown > 0)
            return Loc.GetString("wf-mining-beam-status-cooldown", ("seconds", (int) MathF.Ceiling(dish.Cooldown)));
        if (firing)
            return Loc.GetString("wf-mining-beam-status-firing");
        if (dish.Active)
            return Loc.GetString("wf-mining-beam-status-firing-other");
        return Loc.GetString(target == null ? "wf-mining-beam-status-idle" : "wf-mining-beam-status-ready");
    }
}
