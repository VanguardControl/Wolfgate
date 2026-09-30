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
        SeparationOverride = 20;
        var preview = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true,
            SeparationOverride = 8,
        };
        AddChild(preview);
        preview.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-forward"), HorizontalAlignment = HAlignment.Center });
        preview.AddChild(_dial = new ShieldDial());
        preview.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-aft"), HorizontalAlignment = HAlignment.Center });
        preview.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-click-bearing"), HorizontalAlignment = HAlignment.Center });
        var settings = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical, MinWidth = 350, MaxWidth = 390,
            HorizontalExpand = true, SeparationOverride = 12,
        };
        AddChild(settings);
        settings.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-heading") });
        settings.AddChild(_status = new RichTextLabel());
        settings.AddChild(new RichTextLabel { Text = Loc.GetString("wf-shield-helm-bearing") });
        settings.AddChild(_direction = new FloatSpinBox(1f, 1)
        {
            Value = 0f, IsValid = value => float.IsFinite(value) && value >= 0f && value < 360f,
        });
        settings.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-arc") });
        settings.AddChild(_arc = new FloatSpinBox(5f, 0)
        {
            Value = 90f, IsValid = value => float.IsFinite(value) && value >= 30f && value <= 360f,
        });
        settings.AddChild(_amount = new RichTextLabel());
        settings.AddChild(_concentration = new Slider
        {
            MinValue = 0f, MaxValue = 100f, Rounded = true, MinHeight = 28, HorizontalExpand = true,
        });
        settings.AddChild(_strength = new RichTextLabel());
        settings.AddChild(_unprotected = new RichTextLabel());
        _unprotected.SetMessage(Loc.GetString("wf-shield-helm-unprotected"), Color.Orange);
        settings.AddChild(_draft = new RichTextLabel());
        settings.AddChild(_apply = new Button { Text = Loc.GetString("wf-shield-helm-apply"), Disabled = true });
        settings.AddChild(_reset = new Button { Text = Loc.GetString("wf-shield-helm-reset"), Disabled = true });
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

    /// <summary>Accepts shared helm state without resetting an unchanged draft on every health update.</summary>
    public void UpdateState(WFShipShieldShuntState? state, float helmRotation)
    {
        var changed = _state == null || state == null || _state.Available != state.Available ||
            _state.DirectionRadians != state.DirectionRadians || _state.Concentration != state.Concentration ||
            _state.ArcRadians != state.ArcRadians || _helmRotation != helmRotation;
        _state = state;
        _helmRotation = helmRotation;
        _dial.Available = state is { Available: true };
        _dial.Health = state?.Health ?? 0f;
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
        _strength.Text = Loc.GetString("wf-shield-helm-strength", ("strength", MathF.Round(boost * 100f)),
            ("outside", MathF.Round((_arc.Value >= 360f ? 1f : 1f - concentration) * 100f)));
        _unprotected.Visible = concentration >= 1f && _arc.Value < 360f;
        _draft.Text = Loc.GetString(_dirty ? "wf-shield-helm-draft" : _pending ? "wf-shield-helm-pending" : "wf-shield-helm-live");
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
        public float Health;
        public bool Available;
        private readonly Vector2[] _triangle = new Vector2[3];

        public ShieldDial()
        {
            MinSize = new Vector2(360f, 360f);
            HorizontalExpand = true;
            VerticalExpand = true;
            MouseFilter = MouseFilterMode.Stop;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            var center = (Vector2) PixelSize / 2f;
            var radius = MathF.Min(PixelSize.X, PixelSize.Y) * 0.42f;
            handle.DrawCircle(center, radius, new Color(0.12f, 0.2f, 0.25f), false);
            var tint = WFShipShieldEffects.HealthColor(Health);
            for (var segment = 0; segment < 120; segment++)
            {
                var angle = segment * MathF.Tau / 120f;
                var next = (segment + 1) * MathF.Tau / 120f;
                var point = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var strength = Available ? WFShipShieldShuntMath.StrengthMultiplier(point, Vector2.Zero, Direction, Concentration, Arc) : 0f;
                if (strength <= 0f)
                    continue;
                var opacity = Math.Clamp(0.16f + strength * 0.18f, 0.16f, 0.9f);
                handle.DrawLine(Point(angle, radius), Point(next, radius), tint.WithAlpha(opacity));
                handle.DrawLine(Point(angle, radius - 3f * UIScale), Point(next, radius - 3f * UIScale), tint.WithAlpha(opacity));
                if (Concentration > 0f)
                {
                    _triangle[0] = center;
                    _triangle[1] = Point(angle, radius - 6f * UIScale);
                    _triangle[2] = Point(next, radius - 6f * UIScale);
                    handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _triangle,
                        tint.WithAlpha(opacity * 0.12f));
                }
            }
            handle.DrawLine(center, Point(Direction, radius * 0.88f), Color.White);
            handle.DrawLine(center + new Vector2(-12f, 14f) * UIScale, center - new Vector2(0f, 20f) * UIScale, Color.White);
            handle.DrawLine(center - new Vector2(0f, 20f) * UIScale, center + new Vector2(12f, 14f) * UIScale, Color.White);
            return;
            Vector2 Point(float angle, float distance) => center + new Vector2(MathF.Cos(angle), -MathF.Sin(angle)) * distance;
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            if (!Available || args.Function != EngineKeyFunctions.UIClick)
                return;
            var delta = args.RelativePosition - Size / 2f;
            var degrees = MathF.Atan2(delta.X, -delta.Y) * 180f / MathF.PI;
            BearingRequested?.Invoke((degrees + 360f) % 360f);
            args.Handle();
        }
    }
}
