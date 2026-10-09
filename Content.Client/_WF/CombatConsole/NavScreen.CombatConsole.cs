using Robust.Client.UserInterface.Controls;
using Robust.Shared.Physics.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Client._WF.CombatConsole;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    /// <summary>Arranges flight telemetry, radar and navigation switches in separate instrument banks.</summary>
    public void WfRefitInstruments()
    {
        ReadonlyDisplay.Margin = new Thickness(0);
        NavDisplayLabel.MinHeight = 24;
        NavDisplayLabel.VerticalExpand = false;
        NavDisplayLabel.HorizontalExpand = true;
        NavDisplayLabel.HorizontalAlignment = HAlignment.Stretch;
        NavDisplayLabel.ClipText = true;
        ReadonlyDisplay.Visible = false;
        var speed = new WFGlassGauge("wf-gauge-speed", () => WfMotionReading(-1));
        var yaw = new WFGlassGauge("wf-gauge-yaw", () => WfMotionReading(2));
        var lateral = new WFGlassGauge("wf-gauge-lateral", () => WfMotionReading(0), true);
        var forward = new WFGlassGauge("wf-gauge-forward", () => WfMotionReading(1), true);
        var altitude = new WFGlassGauge("wf-gauge-altitude", () =>
        {
            double? value = GridAltitude.Visible && _entManager.TryGetComponent(_shuttleEntity, out TransformComponent? _) && _shuttleEntity is { } shuttle ? _zLevels.GetAbsoluteAltitude(shuttle) : null;
            var limit = WFGaugeScale.Ceiling(Math.Abs(value ?? 0), 10);
            return WFGaugeReading.Number(value, -limit, limit, "wf-gauge-unit-altitude", 2);
        }, true);
        var climb = new WFGlassGauge("wf-gauge-climb", () =>
        {
            double? value = GridVerticalVelocity.Visible && _entManager.TryGetComponent(_shuttleEntity, out CEZPhysicsComponent? physics)
                ? physics.Velocity : null;
            var limit = WFGaugeScale.Ceiling(Math.Abs(value ?? 0), 10);
            return WFGaugeReading.Number(value, -limit, limit, "wf-gauge-unit-climb", 2);
        }, true);
        altitude.Name = "WfAltitudeGauge";
        climb.Name = "WfClimbGauge";
        altitude.Visible = GridAltitude.Visible;
        climb.Visible = GridVerticalVelocity.Visible;
        GridAltitude.OnVisibilityChanged += control => altitude.Visible = control.Visible;
        GridVerticalVelocity.OnVisibilityChanged += control => climb.Visible = control.Visible;
        var travel = new WFGlassReadout(GridTravelState) { Visible = GridTravelState.Visible };
        GridTravelState.OnVisibilityChanged += control => travel.Visible = control.Visible;
        ShuttleDesignation.ClipText = GridPosition.ClipText = GridTravelState.ClipText = true;
        var telemetry = Panel("wf-console-telemetry", Column(Row(speed, yaw), forward, lateral,
            new WFGlassReadout(Column(ShuttleDesignation, GridPosition)), altitude, climb,
            travel, ReadonlyDisplay));
        var motion = Panel("wf-console-motion", Column(DampenerModeButtons, MaximumShuttleSpeedBox));
        var gyro = Panel("wf-console-bearing", new WFHeadingInstrument(() =>
            _entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform)
                ? (-_xformSystem.GetWorldRotation(transform)).Reduced().FlipPositive().Degrees
                : null));
        var left = Column(gyro, telemetry);
        left.MinWidth = left.MaxWidth = 280;

        foreach (var control in ReadonlyDisplay.Children)
        {
            if (control is Label { HorizontalExpand: true } value)
                value.ClipText = true;
        }
        var rangeInput = MaximumIFFDistanceValue.GetChild(0).GetChild(1);
        rangeInput.MinWidth = 72;
        rangeInput.HorizontalExpand = false;
        var sensors = Panel("wf-console-sensors", Column(IFFToggle, IFFDetailedToggle, DockToggle,
            IffSearchBox, MaximumIFFDistanceBox));
        NetworkPortsBox.Columns = 2;
        NetworkPortsBox.Rows = 4;
        NetworkPortsBox.HorizontalAlignment = HAlignment.Stretch;
        NetworkPortsBox.Margin = new Thickness(0);
        foreach (var control in NetworkPortsBox.Children)
        {
            control.SetWidth = 110;
            control.HorizontalExpand = true;
        }
        var right = Column(motion, sensors, Panel("wf-console-orbit", WfOrbitButton),
            Panel("wf-console-auxiliary", NetworkPortsBox));
        right.MinWidth = right.MaxWidth = 260;
        var radar = Scope("wf-console-navigation-scope", NavRadar);
        var leftScroll = Scroll(left);
        leftScroll.HorizontalExpand = false;
        leftScroll.SetWidth = 280;
        var rightScroll = Scroll(right);
        rightScroll.HorizontalExpand = false;
        rightScroll.SetWidth = 260;
        DisposeAllChildren();
        SeparationOverride = 8;
        AddChild(leftScroll);
        AddChild(radar);
        AddChild(rightScroll);
    }

    private WFGaugeReading WfMotionReading(int axis)
    {
        double? value = null;
        if (_entManager.TryGetComponent(_shuttleEntity, out PhysicsComponent? physics) &&
            _entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform))
        {
            var velocity = (-_xformSystem.GetWorldRotation(transform)).RotateVec(physics.LinearVelocity);
            value = axis switch { -1 => velocity.Length(), 0 => velocity.X, 1 => velocity.Y,
                _ => -MathHelper.RadiansToDegrees(physics.AngularVelocity) };
        }
        var limit = WFGaugeScale.Ceiling(Math.Abs(value ?? 0), axis == 2 ? 90 : 100);
        return WFGaugeReading.Number(value, axis == -1 ? 0 : -limit, limit,
            axis == 2 ? "wf-gauge-unit-turn" : "wf-gauge-unit-speed", 1);
    }
}
