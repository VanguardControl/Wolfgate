using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Shared._WF.Vehicles.Audio;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Weapons.Ranged.Events;
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
    private float _speedValue;
    private float _speedLimit = 60f;
    private float _rpmValue;
    private float _fuelValue;
    private float _batteryValue;
    private Label _engineState = default!;
    private Label _weapon = default!;
    private Label _ammo = default!;
    private Button _ignition = default!;
    private Button _brake = default!;
    private Button _accessories = default!;
    private Button _headlights = default!;
    private EntityUid? _vehicle;

    public override void Initialize()
    {
        _panel = new WFInstrumentPanel(8)
        {
            Name = "WFVehicleHud",
            Visible = false,
            SetWidth = 680,
            HorizontalAlignment = Control.HAlignment.Left,
            VerticalAlignment = Control.VAlignment.Top,
        };
        _role = WFInstrumentTheme.Label("wf-vehicle-hud-driver");
        _engineState = new Label();
        _weapon = new Label { MaxWidth = 250, ClipText = true };
        _ammo = new Label();
        _ignition = WFInstrumentTheme.Button("wf-vehicle-hud-start");
        _accessories = WFInstrumentTheme.Button("wf-vehicle-hud-accessories");
        _headlights = WFInstrumentTheme.Button("wf-vehicle-hud-headlights");
        _brake = WFInstrumentTheme.Button("wf-vehicle-hud-brake");
        var speed = new WFGlassGauge("wf-vehicle-gauge-speed", () =>
            WFGaugeReading.Number(_speedValue, 0, _speedLimit, "wf-vehicle-unit-speed"));
        var rpm = new WFGlassGauge("wf-vehicle-gauge-rpm", () =>
            WFGaugeReading.Number(_rpmValue, 0, 3000, "wf-vehicle-unit-rpm"));
        var fuel = new WFGlassGauge("wf-vehicle-gauge-fuel", () =>
            WFGaugeReading.Number(_fuelValue, 0, 100, "wf-vehicle-unit-percent", tint:
                _fuelValue < 15 ? WFInstrumentTheme.Red : null));
        var battery = new WFGlassGauge("wf-vehicle-gauge-battery", () =>
            WFGaugeReading.Number(_batteryValue, 0, 100, "wf-vehicle-unit-percent", tint:
                _batteryValue < 15 ? WFInstrumentTheme.Red : null), strip: true)
            { CompactStrip = true, SetHeight = 44 };
        var switches = WFInstrumentTheme.Column(
            WFInstrumentTheme.Row(_role, _engineState), battery,
            WFInstrumentTheme.Row(_ignition, _accessories),
            WFInstrumentTheme.Row(_headlights, _brake), _weapon, _ammo);
        switches.SetWidth = 280;
        _panel.AddChild(WFInstrumentTheme.Row(rpm, speed, fuel, switches));
        WFInstrumentTheme.Install(_panel);
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
        _speedValue = speed;
        _speedLimit = mover == null ? 60f : MathF.Max(1f, mover.MaxSpeed * 3.6f);
        _rpmValue = !engine.Running ? 0f : 700f + 2300f *
            (TryComp(uid, out WFVehicleEngineAudioComponent? audio) ? audio.Throttle : Math.Clamp(speed / _speedLimit, 0f, 1f));
        _fuelValue = Math.Clamp(100f * engine.Fuel / MathF.Max(1f, engine.Capacity), 0f, 100f);
        _batteryValue = Math.Clamp(100f * engine.BatteryCharge / MathF.Max(1f, engine.BatteryCapacity), 0f, 100f);
        PositionHud();
        _engineState.Text = Loc.GetString(engine.Running ? "wf-vehicle-hud-running" :
            engine.Powered ? "wf-vehicle-hud-accessory-state" : "wf-vehicle-hud-stopped");
        _ignition.Visible = driver;
        _brake.Visible = driver;
        _accessories.Visible = driver;
        _headlights.Visible = driver;
        _accessories.Text = Loc.GetString(engine.Electrics ? "wf-vehicle-hud-off" : "wf-vehicle-hud-accessories");
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
    /// <summary>Anchors the instrument bank above the live hotbar at the player's UI scale.</summary>
    private void PositionHud()
    {
        var hotbar = _ui.GetActiveUIWidgetOrNull<HotbarGui>();
        var bottom = _ui.PopupRoot.Height - 110;
        if (hotbar is { Visible: true, Height: > 0 })
            bottom = hotbar.GlobalPosition.Y - _ui.PopupRoot.GlobalPosition.Y - 8;
        _panel.SetWidth = MathF.Min(680, MathF.Max(320, _ui.PopupRoot.Width - 16));
        LayoutContainer.SetPosition(_panel, new Vector2(
            MathF.Max(8, (_ui.PopupRoot.Width - _panel.Width) / 2),
            MathF.Max(0, bottom - MathF.Max(158, _panel.Height))));
    }

}
