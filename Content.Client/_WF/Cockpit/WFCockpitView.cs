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
public sealed record WFCockpitParts(Control Camera, Control Alerts, Control Instruments, Control Translation, Control Flight,
    Control Shields, (string Key, Control Content)[] Pages);

/// <summary>A fullscreen instrument surround using the player's real world viewport and chat.</summary>
public sealed partial class WFCockpitView : Control
{
    private readonly WFCockpitLease _lease = new();
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
    private readonly Control _shields;
    private readonly Button _expand;
    private readonly (string Key, Control Content)[] _pages;
    private bool _expanded;
    private bool _restored;

    /// <summary>Creates the cockpit while borrowing all controls through a reversible lease.</summary>
    public WFCockpitView(ShuttleConsoleWindow console, InGameScreen screen, MainViewport viewport, Action exit)
    {
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
        var exitButton = Button("wf-cockpit-exit");
        exitButton.HorizontalExpand = false;
        exitButton.SetWidth = 150;
        exitButton.OnPressed += _ => exit();
        var title = Label("wf-cockpit-title", Accent);
        title.HorizontalExpand = true;
        _top = Column(Row(title, exitButton), parts.Alerts);
        _left = Panel("wf-cockpit-instruments", Scroll(parts.Instruments), true);
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
        foreach (var (page, content) in _pages)
        {
            content.Visible = true;
            _pageHosts[page].Visible = page == key;
        }
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
        IoCManager.Resolve<IEntityManager>().System<Content.Client._WF.Shuttles.Systems.ShuttleExternalCameraSystem>().WfEndCockpitInput();
        _lease.Restore();
        _console.WfSetCockpitActive(false);
        Dispose();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _top.Measure(new Vector2(availableSize.X - 16, availableSize.Y));
        var (left, right, center, height, bottom) = Dimensions(availableSize);
        _translation.Measure(new Vector2(center, height));
        var modesHeight = MeasureGunnery(left, height);
        var flightHeight = bottom;
        _camera.Measure(new Vector2(left, height - flightHeight));
        _left.Measure(new Vector2(left, Math.Max(80, height - flightHeight - _camera.DesiredSize.Y - modesHeight - 12)));
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
        var instrumentsTop = top + ArrangeGunnery(left, top, height);
        _left.Arrange(UIBox2.FromDimensions(new Vector2(8, instrumentsTop), new Vector2(left, Math.Max(80, cameraY - instrumentsTop - 6))));
        _camera.Arrange(UIBox2.FromDimensions(new Vector2(8, cameraY), new Vector2(left, cameraHeight)));
        _flight.Arrange(UIBox2.FromDimensions(new Vector2(8, flightY), new Vector2(left, flightHeight)));
        _translation.Arrange(UIBox2.FromDimensions(new Vector2(centerX, top), new Vector2(center, motionHeight)));
        var worldBounds = UIBox2.FromDimensions(new Vector2(centerX, top + motionHeight + 6),
            new Vector2(center, Math.Max(80, height - bottom - motionHeight - 12)));
        _world.Arrange(worldBounds);
        ArrangeHud(worldBounds, size);
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
        var right = Math.Clamp(size.X * (_expanded ? 0.48f : 0.29f), 340, _expanded ? 840 : 520);
        right = Math.Min(right, Math.Max(300, size.X - left - 532));
        var center = Math.Max(160, size.X - left - right - 32);
        var height = Math.Max(320, size.Y - _top.DesiredSize.Y - 20);
        var bottom = Math.Clamp(size.Y * 0.27f, 218, 250);
        return (left, right, center, height, bottom);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _console.WfCockpitRefresh();
    }
}
