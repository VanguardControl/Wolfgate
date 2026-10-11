using System.Linq;
using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Cockpit;

/// <summary>The console controls assigned to the permanent HUD and selectable MFD pages.</summary>
public sealed record WFCockpitParts(Control Camera, Control Alerts, Control Instruments, Control Fuel, Control Translation, Control Flight,
    Control Shields, Control Tcas, (string Key, Control Content)[] Pages);

/// <summary>A fullscreen instrument surround using the player's real world viewport and chat.</summary>
public sealed partial class WFCockpitView : Control
{
    private readonly WFCockpitLease _lease;
    private readonly ShuttleConsoleWindow _console;
    private readonly Control _top;
    private readonly Control _left;
    private readonly Control _world;
    private readonly Control _translation;
    private readonly Dictionary<string, Button> _selectors = new();
    private readonly Dictionary<string, Control> _pageHosts = new();
    private readonly Control _right;
    private readonly Control _comms;
    private readonly Control _flight;
    private readonly Control _camera;
    private readonly Control _tcas;
    private readonly Control _shields;
    private readonly Button _expand;
    private readonly (string Key, Control Content)[] _pages;
    private readonly (Control Dial, float Full)[] _dials;
    private bool _expanded;
    private bool _restored;
    private bool _compactDials;

    /// <summary>Whether the dial column is too short for full-size dials and shows the smaller faces.</summary>
    public bool CompactDials => _compactDials;

    /// <summary>Creates the cockpit, borrowing every control through the caller's lease so a failed build can be undone.</summary>
    public WFCockpitView(ShuttleConsoleWindow console, InGameScreen screen, MainViewport viewport, Action exit, WFCockpitLease lease)
    {
        _lease = lease;
        _console = console;
        Name = "WFCockpitView";
        HorizontalExpand = VerticalExpand = true;
        RectClipContent = true;
        var chat = screen.ChatBox;
        var hiddenHud = new Control { Visible = false };
        hiddenHud.AddStyleClass("WfNativeStyle");
        var normalHud = new List<Control>(screen.Children);
        _lease.Clear(screen);
        foreach (var control in normalHud)
            hiddenHud.AddChild(_lease.Take(control, restoreVisibility: false));
        AddChild(hiddenHud);
        var world = _lease.Take(viewport, restoreVisibility: false);
        world.VerticalExpand = true;
        var communications = _lease.Take(chat, restoreVisibility: false);
        communications.VerticalExpand = true;
        if (chat is ResizableChatBox resizable)
        {
            resizable.WfCockpitDocked = true;
            _lease.Remember(() => resizable.WfCockpitDocked = false);
        }
        chat.AddStyleClass("WfNativeStyle");
        viewport.AddStyleClass("WfNativeStyle");
        _lease.Remember(() =>
        {
            chat.RemoveStyleClass("WfNativeStyle");
            viewport.RemoveStyleClass("WfNativeStyle");
        });
        var wasVisible = console.Visible;
        _lease.Remember(() => console.Visible = wasVisible);
        console.Visible = false;
        var parts = console.WfBuildCockpit(_lease);
        _pages = parts.Pages;
        _dials = WFCockpitLease.Descendants(parts.Instruments)
            .Where(control => control.Name is "CockpitHeading" or "CockpitVelocity" or "CockpitYaw")
            .Select(control => (control, control.SetHeight)).ToArray();
        _top = Column(BuildHeader(exit), parts.Alerts);
        _tcas = parts.Tcas;
        var dials = Column(Label("wf-cockpit-instruments", Accent), parts.Instruments);
        dials.SeparationOverride = 3;
        var instrumentScroll = Scroll(dials);
        instrumentScroll.Name = "CockpitInstrumentScroll";
        var instruments = Column(instrumentScroll, parts.Fuel);
        instruments.SeparationOverride = 3;
        instruments.VerticalExpand = true;
        _left = new WFInstrumentPanel
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new WFConsoleFrameStyleBox(6, housing: true),
        };
        _left.AddChild(instruments);
        _world = world;
        _translation = parts.Translation;
        _comms = Panel("wf-cockpit-comms", communications, true);
        var flight = Column(Scroll(parts.Flight), console.WfCockpitStatus());
        flight.VerticalExpand = true;
        _flight = Panel("wf-cockpit-flight", flight, true);
        _camera = parts.Camera;
        _shields = Panel("wf-cockpit-shields", Scroll(parts.Shields), true);
        var selectors = new BoxContainer { Name = "CockpitMfdSelectors", HorizontalExpand = true, SeparationOverride = 2 };
        var group = new ButtonGroup();
        var display = new Control { HorizontalExpand = true, VerticalExpand = true, RectClipContent = true };
        foreach (var (key, content) in _pages)
        {
            var button = Button(key + "-short", true);
            button.AddStyleClass("WfCompact");
            button.ToolTip = Loc.GetString(key);
            button.Group = group;
            button.OnPressed += _ => SelectPage(key);
            button.Pressed = key == "wf-cockpit-nav";
            selectors.AddChild(button);
            _selectors.Add(key, button);
            content.HorizontalExpand = content.VerticalExpand = true;
            _pageHosts.Add(key, content);
            display.AddChild(content);
        }
        _expand = Button("wf-cockpit-expand-short", true);
        _expand.AddStyleClass("WfCompact");
        _expand.ToolTip = Loc.GetString("wf-cockpit-expand");
        _expand.HorizontalExpand = false;
        _expand.Name = "CockpitMfdExpand";
        _expand.SetWidth = 80;
        _expand.OnToggled += args =>
        {
            _expanded = args.Pressed;
            _expand.Text = Loc.GetString(_expanded ? "wf-cockpit-collapse-short" : "wf-cockpit-expand-short");
            _expand.ToolTip = Loc.GetString(_expanded ? "wf-cockpit-collapse" : "wf-cockpit-expand");
            InvalidateMeasure();
        };
        var caption = Label("wf-cockpit-mfd", Accent);
        caption.HorizontalExpand = true;
        var mfd = Column(Row(caption, _expand), selectors, display);
        mfd.VerticalExpand = true;
        _right = new WFInstrumentPanel { HorizontalExpand = true, VerticalExpand = true };
        _right.AddChild(mfd);
        _left.Name = "CockpitInstruments";
        _flight.Name = "CockpitFlight";
        var cameraName = _camera.Name;
        _lease.Remember(() => _camera.Name = cameraName);
        _camera.Name = "CockpitCamera";
        _right.Name = "CockpitMfd";
        _comms.Name = "CockpitComms";
        _shields.Name = "CockpitShields";
        _translation.Name = "CockpitTranslation";
        AddChild(_top);
        AddChild(_left);
        AddChild(_tcas);
        AddChild(_world);
        InitializeHud(screen, hiddenHud);
        AddChild(_translation);
        AddChild(_right);
        AddChild(_comms);
        AddChild(_flight);
        AddChild(_camera);
        AddChild(_shields);
        InitializeGunnery(viewport);
        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);
        screen.AddChild(this);
        SelectPage("wf-cockpit-nav");
        console.WfSendCockpitGunnery(new Content.Shared._WF.Cockpit.WFCockpitGunnerySessionMessage(true));
        console.WfSetCockpitActive(true);
        Install(this);
    }

    /// <summary>Switches only the MFD; cameras, flight controls, shield controls and chat stay in place.</summary>
    public void SelectPage(string key)
    {
        foreach (var (page, _) in _pages)
            _pageHosts[page].Visible = page == key;
        foreach (var (page, button) in _selectors)
            button.Pressed = page == key;
        _console.WfCockpitShipVisible(key == "wf-cockpit-ship");
        _console.WfCockpitPageSelected(key);
    }

    /// <summary>Restores the existing HUD and console before disposing the temporary surround.</summary>
    public void Restore()
    {
        if (_restored)
            return;
        _restored = true;
        Parent?.RemoveChild(this);
        try
        {
            IoCManager.Resolve<IEntityManager>().System<Content.Client._WF.Shuttles.Systems.ShuttleExternalCameraSystem>().WfEndCockpitInput();
        }
        finally
        {
            _lease.Restore();
            _console.WfSetCockpitActive(false);
            Dispose();
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _top.Measure(new Vector2(availableSize.X - 16, availableSize.Y));
        var (left, right, center, height, bottom) = Dimensions(availableSize);
        _translation.Measure(new Vector2(center, height));
        _tcas.Measure(new Vector2(left, height));
        var modesHeight = MeasureGunnery(left, height - _tcas.DesiredSize.Y - 6);
        var flightHeight = bottom;
        _camera.Measure(new Vector2(left, height - flightHeight));
        var leftHeight = Math.Max(80, height - flightHeight - _camera.DesiredSize.Y - modesHeight - _tcas.DesiredSize.Y - 18);
        if (!_showGunnery)
            FitDials(leftHeight);
        _expand.Disabled = !_expanded && MfdWidth(availableSize, left, true) - MfdWidth(availableSize, left, false) < 1;
        _left.Measure(new Vector2(left, leftHeight));
        _flight.Measure(new Vector2(left, flightHeight));
        _right.Measure(new Vector2(right, height));
        var worldSize = new Vector2(center, Math.Max(80, height - bottom - _translation.DesiredSize.Y - 12));
        _world.Measure(worldSize);
        MeasureHud(worldSize, availableSize);
        _comms.Measure(new Vector2((center - 6) * 0.43f, bottom));
        _shields.Measure(new Vector2((center - 6) * 0.57f, bottom));
        return availableSize;
    }

    protected override Vector2 ArrangeOverride(Vector2 size)
    {
        var (left, right, center, height, bottom) = Dimensions(size);
        var top = _top.DesiredSize.Y + 12;
        var centerX = left + 16;
        var motionHeight = _translation.DesiredSize.Y;
        var bottomY = size.Y - bottom - 8;
        _top.Arrange(UIBox2.FromDimensions(new Vector2(8), new Vector2(size.X - 16, _top.DesiredSize.Y)));
        var flightHeight = bottom;
        var cameraHeight = _camera.DesiredSize.Y;
        var flightY = size.Y - flightHeight - 8;
        var cameraY = flightY - cameraHeight - 6;
        var tcasHeight = _tcas.DesiredSize.Y;
        var tcasY = (_showGunnery ? size.Y - 8 : cameraY - 6) - tcasHeight;
        var instrumentsTop = top + ArrangeGunnery(left, top, height - tcasHeight - 6);
        _left.Arrange(UIBox2.FromDimensions(new Vector2(8, instrumentsTop), new Vector2(left, Math.Max(1, tcasY - instrumentsTop - 6))));
        _tcas.Arrange(UIBox2.FromDimensions(new Vector2(8, tcasY), new Vector2(left, tcasHeight)));
        _camera.Arrange(UIBox2.FromDimensions(new Vector2(8, cameraY), new Vector2(left, cameraHeight)));
        _flight.Arrange(UIBox2.FromDimensions(new Vector2(8, flightY), new Vector2(left, flightHeight)));
        _translation.Arrange(UIBox2.FromDimensions(new Vector2(centerX, top), new Vector2(center, motionHeight)));
        var worldBounds = UIBox2.FromDimensions(new Vector2(centerX, top + motionHeight + 6),
            new Vector2(center, Math.Max(80, height - bottom - motionHeight - 12)));
        _world.Arrange(worldBounds);
        ArrangeHud(worldBounds);
        _right.Arrange(UIBox2.FromDimensions(new Vector2(size.X - right - 8, top), new Vector2(right, height)));
        var communications = (center - 6) * 0.43f;
        _comms.Arrange(UIBox2.FromDimensions(new Vector2(centerX, bottomY), new Vector2(communications, bottom)));
        _shields.Arrange(UIBox2.FromDimensions(new Vector2(centerX + communications + 6, bottomY),
            new Vector2(center - communications - 6, bottom)));
        return size;
    }

    private (float Left, float Right, float Center, float Height, float Bottom) Dimensions(Vector2 size)
    {
        var left = _showGunnery ? Math.Clamp(size.X * 0.27f, 320, 420) : Math.Clamp(size.X * 0.21f, 220, 320);
        var right = MfdWidth(size, left, _expanded);
        var center = Math.Max(160, size.X - left - right - 32);
        var height = Math.Max(320, size.Y - _top.DesiredSize.Y - 20);
        var bottom = Math.Clamp(size.Y * 0.27f, 218, 250);
        return (left, right, center, height, bottom);
    }

    /// <summary>The MFD width, capped so the world view and lower panels keep their room.</summary>
    private static float MfdWidth(Vector2 size, float left, bool expanded)
    {
        var width = Math.Clamp(size.X * (expanded ? 0.48f : 0.29f), 340, expanded ? 840 : 520);
        return Math.Min(width, Math.Max(300, size.X - left - 532));
    }

    /// <summary>Swaps the large dials for compact ones while the instrument column is too short to show them whole.</summary>
    private void FitDials(float columnHeight)
    {
        var compact = WFCockpitInstrumentSizing.UseCompact(columnHeight);
        if (compact == _compactDials)
            return;
        _compactDials = compact;
        foreach (var (dial, full) in _dials)
        {
            dial.SetHeight = compact ? WFCockpitInstrumentSizing.CompactHeight(full) : full;
            for (var parent = dial.Parent; parent != null && parent != this; parent = parent.Parent)
                parent.InvalidateMeasure();
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _console.WfCockpitRefresh();
    }
}
