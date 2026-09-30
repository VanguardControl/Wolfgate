using System.Numerics;
using Content.Shared._WF.ShipShields;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._WF.ShipShields;

/// <summary>Previews and commits arbitrary shield allocation bearings at the ship's helm.</summary>
public sealed class WFShipShieldShuntScreen : BoxContainer
{
    private readonly FloatSpinBox _direction;
    private readonly FloatSpinBox _arc;
    private readonly Slider _concentration;
    private readonly RichTextLabel _status;
    private readonly RichTextLabel _amount;
    private readonly RichTextLabel _strength;
    private readonly RichTextLabel _outside;
    private readonly ProgressBar _health;
    private readonly Label _previewStatus;
    private readonly BoxContainer _settings;
    private readonly RichTextLabel _draft;
    private readonly RichTextLabel _unprotected;
    private readonly Button _apply;
    private readonly Button _reset;
    private readonly ShieldDial _dial;
    private WFShipShieldShuntState? _state;
    private float _helmRotation;
    private bool _updating;
    private bool _dirty;
    private bool _pending;

    /// <summary>Only Apply or Reset sends a network request.</summary>
    public event Action<float, float, float>? AllocationRequested;

    /// <summary>Creates a helm-relative bearing dial, allocation slider and protected-arc controls.</summary>
    public WFShipShieldShuntScreen()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 12;
        Margin = new Thickness(12);
        var header = Column(6);
        header.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-heading") });
        header.AddChild(_status = new RichTextLabel());
        header.AddChild(_health = new ProgressBar
        {
            MinValue = 0f, MaxValue = 1f, MinHeight = 6,
            BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#1B303C") },
            ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#58D6EC") },
        });
        AddChild(Card(header));

        var body = new BoxContainer { SeparationOverride = 12, VerticalExpand = true };
        AddChild(body);
        var preview = Column(8);
        preview.AddChild(_previewStatus = new Label { HorizontalAlignment = HAlignment.Center });
        preview.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-forward"), HorizontalAlignment = HAlignment.Center });
        preview.AddChild(_dial = new ShieldDial());
        preview.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-aft"), HorizontalAlignment = HAlignment.Center });
        preview.AddChild(new RichTextLabel { Text = Loc.GetString("wf-shield-helm-click-bearing") });
        body.AddChild(Card(preview));

        _settings = Column(12);
        _settings.MinWidth = 340;
        _settings.MaxWidth = 360;
        body.AddChild(_settings);
        _direction = new FloatSpinBox(1f, 1)
        {
            Value = 0f, IsValid = value => float.IsFinite(value) && value >= 0f && value < 360f,
            HorizontalExpand = true,
        };
        _arc = new FloatSpinBox(5f, 0)
        {
            Value = 90f, IsValid = value => float.IsFinite(value) && value >= 30f && value <= 360f,
            HorizontalExpand = true,
        };
        var direction = Column(8);
        direction.AddChild(Field("wf-shield-helm-bearing", _direction));
        var bearings = new BoxContainer { SeparationOverride = 4 };
        foreach (var (key, bearing) in new[] { ("fore", 0f), ("starboard", 90f), ("aft", 180f), ("port", 270f) })
        {
            var button = QuickButton($"wf-shield-helm-preset-{key}");
            button.OnPressed += _ => { _direction.Value = bearing; Edited(); };
            bearings.AddChild(button);
        }
        direction.AddChild(bearings);
        direction.AddChild(Field("wf-shield-helm-arc", _arc));
        direction.AddChild(new RichTextLabel { Text = Loc.GetString("wf-shield-helm-arc-help") });
        _settings.AddChild(Card(direction));

        var power = Column(8);
        power.AddChild(_amount = new RichTextLabel());
        power.AddChild(_concentration = new Slider
        {
            MinValue = 0f, MaxValue = 100f, Rounded = true, MinHeight = 28, HorizontalExpand = true,
        });
        var amounts = new BoxContainer { SeparationOverride = 4 };
        foreach (var (key, amount) in new[] { ("balanced", 0f), ("half", 50f), ("all", 100f) })
        {
            var button = QuickButton($"wf-shield-helm-power-{key}");
            button.OnPressed += _ => { _concentration.Value = amount; Edited(); };
            amounts.AddChild(button);
        }
        power.AddChild(amounts);
        power.AddChild(new RichTextLabel { Text = Loc.GetString("wf-shield-helm-power-help") });
        _settings.AddChild(Card(power));

        var result = Column(8);
        result.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-result") });
        result.AddChild(_strength = new RichTextLabel());
        result.AddChild(_outside = new RichTextLabel());
        result.AddChild(_unprotected = new RichTextLabel());
        _unprotected.SetMessage(Loc.GetString("wf-shield-helm-unprotected"), Color.Orange);
        _settings.AddChild(Card(result));
        var footer = Column(8);
        footer.AddChild(_draft = new RichTextLabel());
        var actions = new BoxContainer { SeparationOverride = 8 };
        actions.AddChild(_reset = new Button { Text = Loc.GetString("wf-shield-helm-reset"), Disabled = true, MinHeight = 38 });
        actions.AddChild(_apply = new Button
        {
            Text = Loc.GetString("wf-shield-helm-apply"), Disabled = true, MinHeight = 38, HorizontalExpand = true,
            Modulate = Color.FromHex("#83DCEB"),
        });
        footer.AddChild(actions);
        AddChild(Card(footer));
        _direction.OnValueChanged += _ => Edited();
        _arc.OnValueChanged += _ => Edited();
        _concentration.OnValueChanged += _ => Edited();
        _dial.BearingRequested += degrees =>
        {
            _direction.Value = MathF.Round(degrees, 1) % 360f;
            Edited();
        };
        _apply.OnPressed += _ => ApplyAllocation();
        _reset.OnPressed += _ => ResetAllocation();
        UpdateState(null, 0f);
    }

    private static BoxContainer Column(int spacing) => new()
    {
        Orientation = LayoutOrientation.Vertical, SeparationOverride = spacing, HorizontalExpand = true,
    };

    private static PanelContainer Card(Control content)
    {
        var style = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#101C25"), BorderColor = Color.FromHex("#2B414F"),
            BorderThickness = new Thickness(1),
        };
        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, 12);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, 12);
        var panel = new PanelContainer { PanelOverride = style, HorizontalExpand = true };
        panel.AddChild(content);
        return panel;
    }

    private static BoxContainer Field(string label, Control input)
    {
        var row = new BoxContainer { SeparationOverride = 8 };
        row.AddChild(new Label { Text = Loc.GetString(label), MinWidth = 104 });
        row.AddChild(input);
        return row;
    }

    private static Button QuickButton(string label) => new()
    {
        Text = Loc.GetString(label), HorizontalExpand = true, MinHeight = 30,
    };

    /// <summary>Accepts shared helm state without resetting an unchanged draft on every health update.</summary>
    public void UpdateState(WFShipShieldShuntState? state, float helmRotation)
    {
        var changed = _state == null || state == null || _state.Available != state.Available ||
            _state.DirectionRadians != state.DirectionRadians || _state.Concentration != state.Concentration ||
            _state.ArcRadians != state.ArcRadians || _helmRotation != helmRotation;
        _state = state;
        _helmRotation = helmRotation;
        _dial.Available = state is { Available: true };
        _health.Value = Math.Clamp(state?.Health ?? 0f, 0f, 1f);
        if (_health.ForegroundStyleBoxOverride is StyleBoxFlat fill)
            fill.BackgroundColor = WFShipShieldEffects.HealthColor(_health.Value);
        _settings.Visible = state is { Available: true };
        _reset.Disabled = state is not { Available: true };
        _concentration.Disabled = state is not { Available: true };
        _status.Text = state is { Active: true }
            ? Loc.GetString("wf-shield-helm-health", ("health", MathF.Round(state.Health * 100f)))
            : Loc.GetString(state is { Available: true } ? "wf-shield-helm-offline" : "wf-shield-helm-unavailable");
        if (changed || !_dirty)
        {
            _updating = true;
            _direction.Value = state == null ? 0f : WFShipShieldHelmAngles.Bearing(state.DirectionRadians, helmRotation);
            _arc.Value = state == null ? 90f : state.ArcRadians * 180f / MathF.PI;
            _concentration.Value = (state?.Concentration ?? 0f) * 100f;
            _updating = false;
            _dirty = false;
            _pending = false;
        }
        Refresh();
    }

    private void Edited()
    {
        if (_updating)
            return;
        _dirty = true;
        _pending = false;
        Refresh();
    }

    /// <summary>Edits a local preview without sending any network traffic.</summary>
    public void SetDraft(float bearingDegrees, float concentration, float arcDegrees)
    {
        _direction.Value = bearingDegrees;
        _concentration.Value = Math.Clamp(concentration, 0f, 1f) * 100f;
        _arc.Value = arcDegrees;
        Edited();
    }

    /// <summary>Commits the preview through the helm's bound interface.</summary>
    public void ApplyAllocation() => Commit(false);

    /// <summary>Restores balanced allocation through the helm's bound interface.</summary>
    public void ResetAllocation() => Commit(true);

    private void Refresh()
    {
        var concentration = _concentration.Value / 100f;
        var arc = _arc.Value * MathF.PI / 180f;
        _dial.Direction = WFShipShieldHelmAngles.GridDirection(_direction.Value, 0f);
        _dial.Concentration = concentration;
        _dial.Arc = arc;
        _amount.Text = Loc.GetString("wf-shield-helm-concentration", ("amount", _concentration.Value));
        var boost = WFShipShieldShuntMath.StrengthMultiplier(new Vector2(MathF.Cos(_dial.Direction), MathF.Sin(_dial.Direction)),
            Vector2.Zero, _dial.Direction, concentration, arc);
        _strength.SetMessage(Loc.GetString("wf-shield-helm-strength", ("strength", MathF.Round(boost * 100f))), Color.FromHex("#83DCEB"));
        _outside.Text = Loc.GetString("wf-shield-helm-outside",
            ("outside", MathF.Round((_arc.Value >= 360f ? 1f : 1f - concentration) * 100f)));
        _unprotected.Visible = concentration >= 1f && _arc.Value < 360f;
        _draft.Text = Loc.GetString(_dirty ? "wf-shield-helm-draft" : _pending ? "wf-shield-helm-pending" : "wf-shield-helm-live");
        _previewStatus.Text = Loc.GetString(_dirty ? "wf-shield-helm-preview" : "wf-shield-helm-current");
        _previewStatus.Modulate = _dirty ? Color.Orange : Color.FromHex("#83DCEB");
        _apply.Disabled = !_dirty || _state is not { Available: true };
    }

    private void Commit(bool reset)
    {
        if (_state is not { Available: true })
            return;
        if (reset)
        {
            _concentration.Value = 0f;
            _dirty = false;
            Refresh();
        }
        AllocationRequested?.Invoke(WFShipShieldHelmAngles.GridDirection(_direction.Value, _helmRotation),
            _concentration.Value / 100f, _arc.Value * MathF.PI / 180f);
        _dirty = false;
        _pending = true;
        Refresh();
    }

    /// <summary>A clockwise helm-relative bearing selector with the protected arc drawn at preview strength.</summary>
    private sealed class ShieldDial : Control
    {
        public event Action<float>? BearingRequested;
        public float Direction;
        public float Concentration;
        public float Arc = MathF.PI / 2f;
        public bool Available;
        private bool _dragging;
        private readonly Vector2[] _band = new Vector2[6];

        public ShieldDial()
        {
            MinSize = new Vector2(280f, 280f);
            HorizontalExpand = true;
            VerticalExpand = true;
            MouseFilter = MouseFilterMode.Stop;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            var center = (Vector2) PixelSize / 2f;
            var radius = MathF.Min(PixelSize.X, PixelSize.Y) * 0.40f;
            var grid = Color.FromHex("#29404F");
            var tint = Available ? Color.FromHex("#58D6EC") : Color.FromHex("#52616B");
            handle.DrawCircle(center, radius * 0.55f, grid, false);
            handle.DrawCircle(center, radius + 14f * UIScale, grid, false);
            handle.DrawLine(center - new Vector2(radius, 0), center + new Vector2(radius, 0), grid);
            handle.DrawLine(center - new Vector2(0, radius), center + new Vector2(0, radius), grid);
            for (var tick = 0; tick < 72; tick++)
            {
                var angle = tick * MathF.Tau / 72f;
                handle.DrawLine(Point(angle, radius + 18f * UIScale),
                    Point(angle, radius + (tick % 6 == 0 ? 26f : 21f) * UIScale), grid);
            }
            for (var segment = 0; segment < 180; segment++)
            {
                var angle = segment * MathF.Tau / 180f;
                var next = (segment + 0.88f) * MathF.Tau / 180f;
                var point = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var strength = Available ? WFShipShieldShuntMath.StrengthMultiplier(point, Vector2.Zero, Direction, Concentration, Arc) : 0f;
                var width = Math.Clamp(5f + strength * 7f, 5f, 28f) * UIScale;
                _band[0] = Point(angle, radius);
                _band[1] = Point(next, radius);
                _band[2] = Point(angle, radius - width);
                _band[3] = _band[2];
                _band[4] = _band[1];
                _band[5] = Point(next, radius - width);
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _band,
                    strength <= 0f ? Color.FromHex("#49312D") : tint.WithAlpha(Math.Clamp(0.25f + strength * 0.3f, 0.25f, 1f)));
            }
            if (Available)
            {
                if (Arc < MathF.Tau - 0.001f)
                {
                    for (var side = -1; side <= 1; side += 2)
                    {
                        var edge = Direction + side * Arc / 2f;
                        handle.DrawLine(Point(edge, radius * 0.25f), Point(edge, radius), tint.WithAlpha(0.45f));
                    }
                }
                handle.DrawLine(Point(Direction, radius * 0.3f), Point(Direction, radius + 8f * UIScale), Color.White.WithAlpha(0.8f));
                handle.DrawCircle(Point(Direction, radius + 8f * UIScale), 5f * UIScale, Color.White);
            }
            // A fixed bow marker keeps helm-relative bearings readable while the selector moves.
            var bow = center + new Vector2(0f, -24f) * UIScale;
            var port = center + new Vector2(-15f, 18f) * UIScale;
            var aft = center + new Vector2(0f, 10f) * UIScale;
            var starboard = center + new Vector2(15f, 18f) * UIScale;
            handle.DrawLine(bow, port, Color.White);
            handle.DrawLine(port, aft, Color.White);
            handle.DrawLine(aft, starboard, Color.White);
            handle.DrawLine(starboard, bow, Color.White);
            return;
            Vector2 Point(float angle, float distance) => center + new Vector2(MathF.Cos(angle), -MathF.Sin(angle)) * distance;
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            if (!Available || args.Function != EngineKeyFunctions.UIClick)
                return;
            _dragging = true;
            SelectBearing(args.RelativePosition);
            args.Handle();
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            base.KeyBindUp(args);
            if (args.Function == EngineKeyFunctions.UIClick)
                _dragging = false;
        }

        protected override void MouseMove(GUIMouseMoveEventArgs args)
        {
            base.MouseMove(args);
            if (_dragging && Available)
                SelectBearing(args.RelativePosition);
        }

        private void SelectBearing(Vector2 position)
        {
            var delta = position - Size / 2f;
            if (delta.LengthSquared() < 100f)
                return;
            var degrees = MathF.Atan2(delta.X, -delta.Y) * 180f / MathF.PI;
            BearingRequested?.Invoke((degrees + 360f) % 360f);
        }
    }
}
