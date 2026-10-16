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
using Robust.Shared.Timing;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>Seat-specific driving and gunnery instruments with server-authorized vehicle controls.</summary>
public sealed partial class WFVehicleHudSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private VehicleHardpointAmmoSystem _ammunition = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    [Dependency] private IGameTiming _timing = default!;

    private readonly WFVehicleEngineComponent _unpowered = new();
    private WFVehicleHudWindow _panel = default!;
    private WFGlassGauge[] _gauges = Array.Empty<WFGlassGauge>();
    private WFVehicleLamp _fuelLamp = default!;
    private Button _ejectKey = default!;
    private bool _powered;
    private float _startup;
    private EntityUid? _lastVehicle;
    private Label _role = default!;
    private float _speedValue;
    private float _speedLimit = 60f;
    private float _rpmValue;
    private float _fuelValue;
    private float _batteryValue;
    private Label _engineState = default!;
    private Label _weapon = default!;
    private Label _ammo = default!;
    private WFVehicleLamp _ignition = default!;
    private Button _brake = default!;
    private WFVehicleIgnitionSlot _accessories = default!;
    private Button _headlights = default!;
    private EntityUid? _vehicle;

    public override void Initialize()
    {
        _panel = new WFVehicleHudWindow();
        _role = WFInstrumentTheme.Label("wf-vehicle-hud-driver");
        _engineState = new Label();
        _weapon = new Label { MaxWidth = 250, ClipText = true };
        _ammo = new Label();
        _ignition = new WFVehicleLamp { Legend = Loc.GetString("wf-vehicle-hud-start") };
        _fuelLamp = new WFVehicleLamp { Legend = Loc.GetString("wf-vehicle-fuel-low"),
            MouseFilter = Control.MouseFilterMode.Ignore };
        _ejectKey = WFInstrumentTheme.Button("wf-vehicle-key-remove");
        _accessories = new WFVehicleIgnitionSlot();
        _headlights = WFInstrumentTheme.Button("wf-vehicle-hud-headlights");
        _brake = WFInstrumentTheme.Button("wf-vehicle-hud-brake");
        var speed = new WFGlassGauge("wf-vehicle-gauge-speed", () => ReadGauge(_speedValue, _speedLimit, "wf-vehicle-unit-speed"));
        var rpm = new WFGlassGauge("wf-vehicle-gauge-rpm", () => ReadGauge(_rpmValue, 3000, "wf-vehicle-unit-rpm"));
        var fuel = new WFGlassGauge("wf-vehicle-gauge-fuel", () => ReadGauge(_fuelValue, 100, "wf-vehicle-unit-percent"));
        var battery = new WFGlassGauge("wf-vehicle-gauge-battery", () => ReadGauge(_batteryValue, 100, "wf-vehicle-unit-percent"), strip: true)
            { CompactStrip = true, SetHeight = 44 };
        _gauges = new[] { rpm, speed, fuel, battery };
        var switches = WFInstrumentTheme.Column(
            WFInstrumentTheme.Row(_accessories, _ignition, _fuelLamp), battery,
            WFInstrumentTheme.Row(_headlights, _brake, _ejectKey), _weapon, _ammo);
        switches.SetWidth = 390;
        _role.MouseFilter = _engineState.MouseFilter = Control.MouseFilterMode.Ignore;
        var header = WFInstrumentTheme.Row(_role, _engineState, new Control { HorizontalExpand = true,
            MouseFilter = Control.MouseFilterMode.Ignore }, _panel.Popout);
        header.SetHeight = 24;
        header.MouseFilter = Control.MouseFilterMode.Pass;
        var body = WFInstrumentTheme.Column(header, WFInstrumentTheme.Row(rpm, speed, fuel, switches));
        InitializeVehicleControls(body);
        _panel.AddChild(body);
        WFInstrumentTheme.Install(_panel);
        _ejectKey.OnPressed += _ =>
        {
            if (_vehicle is { } uid)
                RaiseNetworkEvent(new WFVehicleControlRequest(GetNetEntity(uid), false, ejectKey: true));
        };
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
        _panel.Close();
        _panel.Dispose();
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
            else if (TryComp(user, out VehicleLockActionComponent? occupant))
                _vehicle = occupant.Vehicle;
            else if (HasComp<RMCVehicleComponent>(user))
                _vehicle = user;
        }

        // Operator components can still name a vehicle or gun that was just deleted.
        if (TerminatingOrDeleted(selected))
            selected = null;

        if (_vehicle is not { } uid || TerminatingOrDeleted(uid))
        {
            _vehicle = null;
            _panel.Close();
            _powered = false;
            _lastVehicle = null;
            return;
        }
        var hasEngine = TryComp(uid, out WFVehicleEngineComponent? engine);
        engine ??= _unpowered;
        driver &= hasEngine;
        if (!_panel.IsOpen)
            _panel.Open();
        RefreshVehicleControls(uid, frameTime);
        _panel.RefreshPopout();
        var powered = engine.Powered;
        if (powered && (!_powered || _lastVehicle != uid))
            _startup = 0f;
        else if (powered)
            _startup = MathF.Min(WFVehicleInstrumentStartup.Duration, _startup + frameTime);
        _powered = powered;
        _lastVehicle = uid;
        foreach (var gauge in _gauges)
            gauge.Powered = powered;

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
        _accessories.Inserted = engine.IgnitionKey != null;
        _accessories.On = engine.Electrics;
        _accessories.ToolTip = Loc.GetString(engine.IgnitionKey == null ? "wf-vehicle-key-insert" :
            engine.Electrics ? "wf-vehicle-key-turn-off" : "wf-vehicle-key-turn-on");
        _ejectKey.Visible = driver;
        _ejectKey.Disabled = engine.Electrics || engine.IgnitionKey == null;
        _ignition.Disabled = !engine.Electrics || engine.IgnitionKey == null ||
            !engine.Running && (engine.Fuel <= 0 || engine.BatteryCharge < engine.StarterEnergy);
        _ignition.Lit = !_ignition.Disabled;
        _ignition.LensColor = WFInstrumentTheme.Green;
        _fuelLamp.LensColor = _fuelValue < 10 ? WFInstrumentTheme.Red : WFInstrumentTheme.Skin.Caution;
        _fuelLamp.Lit = WFVehicleLamp.FuelWarningLit(_fuelValue, powered, _timing.RealTime.TotalSeconds);
        _headlights.Disabled = !engine.Powered;
        _headlights.Pressed = TryComp(uid, out VehicleSpotlightComponent? headlights) && headlights.Enabled;
        _ignition.Legend = Loc.GetString(engine.Running ? "wf-vehicle-hud-stop" : "wf-vehicle-hud-start");
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
        if (_panel.PositionedByUser || _panel.Parent != _ui.WindowRoot)
            return;
        var hotbar = _ui.GetActiveUIWidgetOrNull<HotbarGui>();
        var bottom = _ui.WindowRoot.Height - 110;
        if (hotbar is { Visible: true, Height: > 0 })
            bottom = hotbar.GlobalPosition.Y - _ui.WindowRoot.GlobalPosition.Y - 8;
        _panel.SetWidth = MathF.Min(760, MathF.Max(320, _ui.WindowRoot.Width - 16));
        LayoutContainer.SetPosition(_panel, new Vector2(
            MathF.Max(8, (_ui.WindowRoot.Width - _panel.Width) / 2),
            MathF.Max(0, bottom - MathF.Max(178, _panel.Height))));
    }

    private WFGaugeReading ReadGauge(float actual, float maximum, string unit)
    {
        return WFGaugeReading.Number(_powered ? WFVehicleInstrumentStartup.Reading(actual, maximum, _startup) : null,
            0, maximum, unit);
    }

}
