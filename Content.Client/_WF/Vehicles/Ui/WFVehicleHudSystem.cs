using System.Numerics;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>Seat-specific driving and gunnery instruments with server-authorized vehicle controls.</summary>
public sealed class WFVehicleHudSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private VehicleHardpointAmmoSystem _ammunition = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private PanelContainer _panel = default!;
    private Label _role = default!;
    private Label _speed = default!;
    private WFVehicleSpeedometer _dial = default!;
    private ProgressBar _fuelBar = default!;
    private Label _engineState = default!;
    private Label _fuel = default!;
    private Label _weapon = default!;
    private Label _ammo = default!;
    private Button _ignition = default!;
    private Button _brake = default!;
    private Button _accessories = default!;
    private Button _headlights = default!;
    private Label _battery = default!;
    private EntityUid? _vehicle;

    public override void Initialize()
    {
        _panel = new PanelContainer
        {
            Name = "WFVehicleHud",
            Visible = false,
            MinSize = new Vector2(250, 150),
            Margin = new Thickness(16, 160, 0, 0),
            HorizontalAlignment = Control.HAlignment.Left,
            VerticalAlignment = Control.VAlignment.Top,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#081013EE"),
                BorderColor = Color.FromHex("#74BFA6"),
                BorderThickness = new Thickness(1),
            },
        };
        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(12),
            SeparationOverride = 6,
        };
        _role = new Label { FontColorOverride = Color.FromHex("#74BFA6") };
        _speed = new Label();
        _dial = new WFVehicleSpeedometer();
        _fuelBar = new ProgressBar { MinValue = 0f, MaxValue = 1f, MinSize = new Vector2(0f, 8f) };
        _engineState = new Label();
        _fuel = new Label();
        _weapon = new Label { MaxWidth = 300, ClipText = true };
        _ammo = new Label();
        _ignition = new Button();
        _battery = new Label();
        _accessories = new Button();
        _headlights = new Button { Text = Loc.GetString("wf-vehicle-hud-headlights") };
        _brake = new Button { Text = Loc.GetString("wf-vehicle-hud-brake") };
        foreach (var control in new Control[] { _role, _dial, _speed, _engineState, _fuel, _fuelBar, _battery, _weapon, _ammo, _ignition, _accessories, _headlights, _brake })
            column.AddChild(control);
        _panel.AddChild(column);
        // Keep instruments above the gameplay viewport, like the other Wolfgate HUDs.
        _ui.PopupRoot.AddChild(_panel);
        LayoutContainer.SetAnchorPreset(_panel, LayoutContainer.LayoutPreset.TopLeft);
        _ignition.OnPressed += _ => SendControl(false);
        _brake.OnPressed += _ => SendControl(true);
        _accessories.OnPressed += _ =>
        {
            if (_vehicle is { } uid)
                RaiseNetworkEvent(new WFVehicleControlRequest(GetNetEntity(uid), false, true));
        };
        _headlights.OnPressed += _ =>
        {
            if (_vehicle is { } uid)
                RaiseNetworkEvent(new VehicleSpotlightToggleRequestEvent(GetNetEntity(uid)));
        };
    }

    private void SendControl(bool brake)
    {
        if (_vehicle is { } uid)
            RaiseNetworkEvent(new WFVehicleControlRequest(GetNetEntity(uid), brake));
    }

    public override void Shutdown()
    {
        _panel.Orphan();
        base.Shutdown();
    }

    public override void FrameUpdate(float frameTime)
    {
        _vehicle = null;
        var driver = false;
        EntityUid? selected = null;
        if (_player.LocalEntity is { } user)
        {
            if (TryComp(user, out RMCVehicleOperatorComponent? op) && op.Vehicle is { } driven)
            {
                _vehicle = driven;
                driver = true;
            }
            else if (TryComp(user, out VehicleWeaponsOperatorComponent? gunner))
            {
                _vehicle = gunner.Vehicle;
                selected = gunner.SelectedWeapon;
            }
        }

        _panel.Visible = _vehicle is { } vehicle && HasComp<WFVehicleEngineComponent>(vehicle);
        if (!_panel.Visible || _vehicle is not { } uid || !TryComp(uid, out WFVehicleEngineComponent? engine))
            return;

        _role.Text = Loc.GetString(driver ? "wf-vehicle-hud-driver" : "wf-vehicle-hud-gunner");
        var speed = 0f;
        if (TryComp(uid, out GridVehicleMoverComponent? mover))
        {
            var heading = mover.Heading;
            var velocity = mover.FreeSteering ? mover.TravelVelocity : heading.ToWorldVec() * mover.CurrentSpeed;
            speed = (velocity + mover.ImpactVelocity).Length() * 3.6f;
        }
        _dial.Fraction = mover == null ? 0f : Math.Clamp(speed / MathF.Max(1f, mover.MaxSpeed * 3.6f), 0f, 1f);
        _engineState.Text = Loc.GetString(engine.Running ? "wf-vehicle-hud-running" :
            engine.Powered ? "wf-vehicle-hud-accessory-state" : "wf-vehicle-hud-stopped");
        _fuelBar.Value = Math.Clamp(engine.Fuel / MathF.Max(1f, engine.Capacity), 0f, 1f);
        _speed.Text = Loc.GetString("wf-vehicle-hud-speed", ("speed", MathF.Round(speed)));
        _fuel.Text = Loc.GetString("wf-vehicle-hud-fuel", ("fuel", MathF.Round(100f * engine.Fuel / MathF.Max(1f, engine.Capacity))));
        _fuel.FontColorOverride = engine.Fuel < engine.Capacity * 0.15f ? Color.OrangeRed : Color.White;
        _ignition.Visible = driver;
        _brake.Visible = driver;
        _accessories.Visible = driver;
        _headlights.Visible = driver;
        _accessories.Text = Loc.GetString(engine.Electrics ? "wf-vehicle-hud-off" : "wf-vehicle-hud-accessories");
        _battery.Text = Loc.GetString("wf-vehicle-hud-battery", ("charge", MathF.Round(100f * engine.BatteryCharge / MathF.Max(1f, engine.BatteryCapacity))));
        _battery.FontColorOverride = engine.BatteryCharge < engine.BatteryCapacity * 0.15f ? Color.OrangeRed : Color.White;
        _headlights.Disabled = !engine.Powered;
        _headlights.Pressed = TryComp(uid, out VehicleSpotlightComponent? headlights) && headlights.Enabled;
        _ignition.Text = Loc.GetString(engine.Running ? "wf-vehicle-hud-stop" : "wf-vehicle-hud-start");
        _brake.Pressed = engine.ParkingBrake;
        _weapon.Visible = !driver;
        _ammo.Visible = !driver;
        _weapon.Text = selected is { } gun ? Loc.GetString("wf-vehicle-hud-weapon", ("weapon", Name(gun))) : Loc.GetString("wf-vehicle-hud-no-weapon");
        _weapon.ToolTip = _weapon.Text;
        _ammo.Text = string.Empty;
        if (selected is { } weapon && TryComp(weapon, out VehicleHardpointAmmoComponent? ammo))
        {
            var count = new GetAmmoCountEvent();
            RaiseLocalEvent(weapon, ref count);
            _ammo.Text = Loc.GetString("wf-vehicle-hud-ammo", ("ready", count.Count),
                ("rounds", _ammunition.GetStoredRounds(ammo, ammo.MagazineSize)));
        }
    }
}
