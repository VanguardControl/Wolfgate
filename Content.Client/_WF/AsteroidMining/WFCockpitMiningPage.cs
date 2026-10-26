using System.Linq;
using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// The cockpit's mining page: the ship's mining laser power on a lever, the rock under the lasers on a heat dial with
/// lamps for its bands, and what the rock holds. It shows the same scan as the gunnery console's mining panel, through
/// the helm's gunnery link, and its selector only shows while the linked ship has mining lasers.
/// </summary>
public sealed partial class WFCockpitMiningPage : BoxContainer, IWFCockpitOptionalPage
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly float[] Steps = { -0.1f, -0.01f, 0.01f, 0.1f };

    /// <summary>How long a new power shows before the server's value, in case the server never applies it.</summary>
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromSeconds(1);

    /// <summary>A dragged lever sends at most this often, and its last setting when it stops.</summary>
    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(0.1);

    private readonly Label _status;
    private readonly WFMiningAnnunciator _lamps;
    private readonly WFMiningPowerLever _lever;
    private readonly WFMiningHeatDial _dial;
    private readonly Control _scan;
    private readonly Label _rock;
    private readonly WFMiningCompositionBar _bar;
    private readonly GridContainer _ores;
    private readonly List<Label> _oreLabels = new();
    private readonly Label _inert;
    private readonly Label _resistance;
    private readonly Label _window;
    private readonly Label _yield;
    private readonly Label _idle;
    private WFMiningBeamGunneryState _state;
    private float? _pending;
    private TimeSpan _pendingUntil;
    private float? _unsent;
    private TimeSpan _nextSend;

    /// <summary>A power the pilot set, for the helm to send to the linked gunnery console.</summary>
    public event Action<float>? PowerRequested;

    public bool Available => _state.Lasers > 0;

    public WFCockpitMiningPage()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = VerticalExpand = true;

        _status = Label("wf-mining-gunnery-idle", Muted);
        _status.ClipText = true;
        _status.HorizontalExpand = true;
        _status.Align = Robust.Client.UserInterface.Controls.Label.AlignMode.Right;
        var caption = Label("wf-mining-gunnery-title", Accent);
        _lamps = new WFMiningAnnunciator();

        _lever = new WFMiningPowerLever();
        _lever.Changed += SetPower;
        _dial = new WFMiningHeatDial();
        var instruments = Row(_lever, _dial);
        instruments.SeparationOverride = 6;
        instruments.MinHeight = 190;

        var steps = Row();
        steps.SeparationOverride = 2;
        foreach (var step in Steps)
        {
            var amount = (int) MathF.Round(MathF.Abs(step) * 100);
            var button = new Button
            {
                Text = Loc.GetString(step > 0 ? "wf-mining-gunnery-power-up" : "wf-mining-gunnery-power-down", ("amount", amount)),
                HorizontalExpand = true,
            };
            button.AddStyleClass("WfCompact");
            button.OnPressed += _ => SetPower(MathF.Round(Math.Clamp((_pending ?? _state.Power) + step, 0f, 1f), 2));
            steps.AddChild(button);
        }

        _rock = Label("wf-mining-gunnery-idle", Cream);
        _rock.ClipText = true;
        _bar = new WFMiningCompositionBar();
        _ores = new GridContainer { Columns = 2, HorizontalExpand = true };
        _inert = Label("wf-mining-gunnery-idle", Muted);
        _resistance = Label("wf-mining-gunnery-idle", Muted);
        _window = Label("wf-mining-gunnery-idle", Muted);
        _yield = Label("wf-mining-gunnery-idle", Green);
        _yield.ToolTip = Loc.GetString("wf-mining-gunnery-yield-help");
        _yield.MouseFilter = MouseFilterMode.Pass;
        var stats = Row(_resistance, _window);
        stats.SeparationOverride = 12;
        var scan = Column(_rock, _bar, _ores, _inert, stats, _yield);
        scan.SeparationOverride = 3;
        _scan = Panel("wf-mining-page-target", scan);
        _scan.Visible = false;
        _idle = Label("wf-mining-gunnery-idle", Muted);
        _idle.ToolTip = Loc.GetString("wf-mining-gunnery-idle-help");
        _idle.MouseFilter = MouseFilterMode.Pass;
        var hint = Label("wf-mining-page-hint", Muted);
        hint.ToolTip = Loc.GetString("wf-mining-page-hint-help");
        hint.MouseFilter = MouseFilterMode.Pass;

        var body = Column(Row(caption, _status), _lamps, instruments, steps, _scan, _idle, hint);
        body.SeparationOverride = 6;
        AddChild(Scroll(body));
    }

    /// <summary>Applies the server's scan; a power the pilot just set keeps showing until the server agrees or a second passes.</summary>
    public void UpdateState(WFMiningBeamGunneryState state)
    {
        _state = state;
        if (_unsent == null && _pending is { } pending &&
            (MathF.Abs(pending - state.Power) < 0.005f || _timing.RealTime > _pendingUntil))
            _pending = null;
        Refresh();
    }

    private void SetPower(float power)
    {
        if (!Available)
            return;
        _pending = power;
        _unsent = power;
        _pendingUntil = _timing.RealTime + PendingTimeout;
        Refresh();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var now = _timing.RealTime;
        if (_unsent is { } power && now >= _nextSend)
        {
            _unsent = null;
            _nextSend = now + SendInterval;
            _pendingUntil = now + PendingTimeout;
            PowerRequested?.Invoke(power);
        }
        else if (_unsent == null && _pending != null && now > _pendingUntil)
        {
            // The server never confirmed the setting, and an unchanged scan isn't resent.
            _pending = null;
            Refresh();
        }
    }

    private void Refresh()
    {
        var state = _state;
        if (state.Lasers == 0)
            return;

        _lever.Power = _pending ?? state.Power;
        var overloaded = state.Cooldown > 0f;
        _status.Text = overloaded
            ? Loc.GetString("wf-mining-gunnery-cooldown", ("seconds", (int) MathF.Ceiling(state.Cooldown)))
            : Loc.GetString("wf-mining-gunnery-lasers", ("count", state.Lasers));
        _status.FontColorOverride = overloaded ? Red : Muted;

        var target = state.Target;
        _dial.SetTarget(target);
        _idle.Visible = target == null;
        _scan.Visible = target != null;
        if (target is not { } rock)
        {
            _lamps.Set(false, false, false, overloaded);
            return;
        }

        _lamps.Set(rock.Charge >= rock.WindowLow && rock.Charge <= rock.WindowHigh,
            rock.Charge > rock.WindowHigh && rock.Charge < WFAsteroidHeat.OverchargeLine, rock.Overcharge > 0f, overloaded);
        _rock.Text = Loc.GetString("wf-mining-gunnery-rock", ("name", rock.Name), ("mass", (int) MathF.Round(rock.Mass)));
        _bar.Ores = rock.Ores;
        // Reuse the ore lines between scans, adding only when a rock has more ores than any before it.
        for (var i = _oreLabels.Count; i < rock.Ores.Length; i++)
        {
            var label = Label("wf-mining-gunnery-idle");
            label.ClipText = true;
            label.HorizontalExpand = true;
            _ores.AddChild(label);
            _oreLabels.Add(label);
        }

        for (var i = 0; i < _oreLabels.Count; i++)
        {
            var label = _oreLabels[i];
            label.Visible = i < rock.Ores.Length;
            if (!label.Visible)
                continue;

            var ore = rock.Ores[i];
            label.FontColorOverride = ore.Color;
            label.Text = Loc.GetString("wf-mining-gunnery-ore", ("name", ore.Name), ("percent", (int) MathF.Round(ore.Fraction * 100)));
        }

        _inert.Text = Loc.GetString("wf-mining-gunnery-inert", ("percent", (int) MathF.Round((1f - rock.Ores.Sum(o => o.Fraction)) * 100)));
        _resistance.Text = Loc.GetString("wf-mining-gunnery-resistance", ("percent", (int) MathF.Round(rock.Resistance * 100)));
        _window.Text = Loc.GetString("wf-mining-page-window", ("low", (int) MathF.Round(rock.WindowLow * 100)),
            ("high", (int) MathF.Round(rock.WindowHigh * 100)));
        _yield.Text = Loc.GetString("wf-mining-gunnery-yield", ("percent", (int) MathF.Round(rock.Yield * 100)));
        _yield.FontColorOverride = rock.Yield >= 0.995f ? Green : Accent;
    }
}

/// <summary>A rock's make-up as one bar, each ore in its colour by share and plain rock after them.</summary>
public sealed class WFMiningCompositionBar : Control
{
    public WFMiningBeamOreEntry[] Ores = Array.Empty<WFMiningBeamOreEntry>();

    public WFMiningCompositionBar()
    {
        MinSize = new Vector2(0, 14);
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        WFMiningGauges.Composition(handle, PixelSizeBox, Ores, UIScale);
    }
}
