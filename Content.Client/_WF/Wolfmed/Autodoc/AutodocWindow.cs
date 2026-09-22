using System.Numerics;
using Content.Client._WF.Wolfmed.Medical;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Wolfmed.Autodoc;

/// <summary>
/// S.A.M.'s window. The left half is the health analyzer's own doll and wound panel; the right half is the
/// procedure list, the queue and the pod's controls.
/// </summary>
public sealed class AutodocWindow : DefaultWindow
{
    private readonly IPrototypeManager _protos = IoCManager.Resolve<IPrototypeManager>();

    private readonly WolfmedBodyDoll _doll = new();
    private readonly WolfmedDiagnosticPanel _diagnostics = new();
    private readonly BoxContainer _procedures;
    private readonly BoxContainer _queueBox;
    private readonly BoxContainer _reservoirBox;
    private readonly Label _statusLabel;
    private readonly Label _stepLabel;
    private readonly Label _diskLabel;
    private readonly Label _moduleLabel;
    private readonly Label _trayLabel;
    private readonly ProgressBar _progress;
    private readonly Button _startButton;
    private readonly Button _pauseButton;
    private readonly Button _abortButton;
    private readonly Button _ejectButton;
    private readonly CheckBox _anaesthesia;
    private readonly Label _queueTitle;

    private TargetBodyPart? _filter;
    private AutodocBuiState? _state;

    public event Action<EntProtoId, TargetBodyPart>? OnQueueAdd;
    public event Action<int>? OnQueueRemove;
    public event Action<int, bool>? OnQueueMove;
    public event Action<AutodocControl>? OnControl;
    public event Action<bool>? OnAnaesthesia;

    public AutodocWindow()
    {
        Title = Loc.GetString("wolfmed-autodoc-window-title");
        MinSize = new Vector2(760, 520);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        Contents.AddChild(root);

        var left = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = 340,
            VerticalExpand = true,
        };
        left.AddChild(_doll);
        left.AddChild(_diagnostics);
        root.AddChild(left);

        var right = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 4,
        };
        root.AddChild(right);

        _statusLabel = new Label { StyleClasses = { "LabelBig" } };
        right.AddChild(_statusLabel);

        _stepLabel = new Label { StyleClasses = { "LabelSubText" } };
        right.AddChild(_stepLabel);

        _progress = new ProgressBar { MinValue = 0f, MaxValue = 1f, MinHeight = 12 };
        right.AddChild(_progress);

        right.AddChild(new Label { Text = Loc.GetString("wolfmed-autodoc-window-procedures") });
        _procedures = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        right.AddChild(new ScrollContainer { VerticalExpand = true, MinHeight = 140, Children = { _procedures } });

        _queueTitle = new Label { Text = Loc.GetString("wolfmed-autodoc-window-queue") };
        right.AddChild(_queueTitle);
        _queueBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        right.AddChild(new ScrollContainer { VerticalExpand = true, MinHeight = 110, Children = { _queueBox } });

        right.AddChild(new Label { Text = Loc.GetString("wolfmed-autodoc-window-reservoir") });
        _reservoirBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        right.AddChild(_reservoirBox);

        _diskLabel = new Label { StyleClasses = { "LabelSubText" } };
        right.AddChild(_diskLabel);
        _moduleLabel = new Label { StyleClasses = { "LabelSubText" } };
        right.AddChild(_moduleLabel);
        _trayLabel = new Label { StyleClasses = { "LabelSubText" } };
        right.AddChild(_trayLabel);

        _anaesthesia = new CheckBox { Text = Loc.GetString("wolfmed-autodoc-window-anaesthesia"), Pressed = true };
        _anaesthesia.OnToggled += args => OnAnaesthesia?.Invoke(args.Pressed);
        right.AddChild(_anaesthesia);

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _startButton = new Button { Text = Loc.GetString("wolfmed-autodoc-window-start") };
        _startButton.OnPressed += _ => OnControl?.Invoke(AutodocControl.Start);
        _pauseButton = new Button { Text = Loc.GetString("wolfmed-autodoc-window-pause") };
        _pauseButton.OnPressed += _ => OnControl?.Invoke(AutodocControl.Pause);
        _abortButton = new Button { Text = Loc.GetString("wolfmed-autodoc-window-abort") };
        _abortButton.OnPressed += _ => OnControl?.Invoke(AutodocControl.Abort);
        _ejectButton = new Button { Text = Loc.GetString("wolfmed-autodoc-window-eject") };
        _ejectButton.OnPressed += _ => OnControl?.Invoke(AutodocControl.Eject);
        buttons.AddChild(_startButton);
        buttons.AddChild(_pauseButton);
        buttons.AddChild(_abortButton);
        buttons.AddChild(_ejectButton);
        right.AddChild(buttons);

        // A click on either doll filters the procedure list to that limb.
        _doll.OnPartSelected += SelectPart;
        _diagnostics.OnPartSelected += SelectPart;
    }

    private void SelectPart(TargetBodyPart part)
    {
        _filter = _filter == part ? null : part;
        _doll.SetSelected(_filter);
        _diagnostics.SetTargetedPart(_filter, false);
        if (_state != null)
            DrawProcedures(_state);
    }

    public void Update(AutodocBuiState state)
    {
        _state = state;

        if (state.Diagnostics != null)
        {
            _doll.SetBody(state.Diagnostics.Body);
            _diagnostics.Visible = true;
            _diagnostics.Populate(state.Diagnostics);
        }
        else
        {
            _doll.SetBody(null);
            _diagnostics.Clear();
            _diagnostics.Visible = false;
        }

        _statusLabel.Text = state.Status;
        _stepLabel.Text = state.CurrentStep is { } step
            ? Loc.GetString("wolfmed-autodoc-window-step", ("step", StepName(step)))
            : string.Empty;
        _progress.Value = state.Progress;

        _diskLabel.Text = Loc.GetString("wolfmed-autodoc-window-disk",
            ("disk", state.DiskProgram ?? Loc.GetString("wolfmed-autodoc-window-disk-none")));
        _moduleLabel.Text = Loc.GetString(state.DefibModule
            ? "wolfmed-autodoc-window-module-installed"
            : "wolfmed-autodoc-window-module-missing");
        _trayLabel.Text = Loc.GetString("wolfmed-autodoc-window-tray",
            ("item", state.TrayItem ?? Loc.GetString("wolfmed-autodoc-window-tray-empty")));

        _anaesthesia.Pressed = state.Anaesthesia;
        _queueTitle.Visible = !state.SelfService;

        var running = state.State is AutodocState.Preparing or AutodocState.Step or AutodocState.Waiting;
        _startButton.Disabled = running || !state.Occupied;
        _pauseButton.Disabled = !running && state.State != AutodocState.Paused;
        _abortButton.Disabled = state.State == AutodocState.Idle;

        DrawProcedures(state);
        DrawQueue(state);
        DrawReservoir(state);
    }

    private void DrawProcedures(AutodocBuiState state)
    {
        _procedures.RemoveAllChildren();
        foreach (var entry in state.Available)
        {
            if (_filter != null && entry.Part != _filter)
                continue;

            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
            var button = new Button
            {
                Text = $"{SurgeryName(entry.Surgery)} ({Loc.GetString($"wolfmed-autodoc-part-{entry.Part.ToString().ToLowerInvariant()}")})",
                HorizontalExpand = true,
                Disabled = !entry.Known || state.State is AutodocState.Preparing or AutodocState.Step or AutodocState.Waiting,
                ToolTip = entry.Known
                    ? SurgeryDescription(entry.Surgery)
                    : Loc.GetString("wolfmed-autodoc-window-locked"),
            };

            var captured = entry;
            button.OnPressed += _ => OnQueueAdd?.Invoke(captured.Surgery, captured.Part);
            row.AddChild(button);
            _procedures.AddChild(row);
        }
    }

    private void DrawQueue(AutodocBuiState state)
    {
        _queueBox.RemoveAllChildren();
        for (var i = 0; i < state.Queue.Count; i++)
        {
            var entry = state.Queue[i];
            var index = i;

            var block = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
            header.AddChild(new Label
            {
                Text = $"{index + 1}. {SurgeryName(entry.Surgery)} ({Loc.GetString($"wolfmed-autodoc-part-{entry.Part.ToString().ToLowerInvariant()}")})",
                HorizontalExpand = true,
            });

            if (!state.SelfService)
            {
                var up = new Button { Text = "^" };
                up.OnPressed += _ => OnQueueMove?.Invoke(index, true);
                var down = new Button { Text = "v" };
                down.OnPressed += _ => OnQueueMove?.Invoke(index, false);
                var remove = new Button { Text = "X" };
                remove.OnPressed += _ => OnQueueRemove?.Invoke(index);
                header.AddChild(up);
                header.AddChild(down);
                header.AddChild(remove);
            }

            block.AddChild(header);
            foreach (var requirement in entry.Requirements)
                block.AddChild(new Label { Text = "  " + requirement, StyleClasses = { "LabelSubText" } });

            _queueBox.AddChild(block);
        }
    }

    private void DrawReservoir(AutodocBuiState state)
    {
        _reservoirBox.RemoveAllChildren();
        foreach (var entry in state.Reservoir)
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
            row.AddChild(new Label { Text = entry.Name, HorizontalExpand = true });
            row.AddChild(new ProgressBar
            {
                MinValue = 0f,
                MaxValue = MathF.Max(1f, entry.Max),
                Value = entry.Volume,
                MinWidth = 120,
                MinHeight = 10,
                Modulate = entry.Usable ? Color.White : Color.DarkGray,
            });
            _reservoirBox.AddChild(row);
        }
    }

    private string SurgeryName(EntProtoId surgery) =>
        _protos.TryIndex(surgery, out EntityPrototype? proto) ? proto.Name : surgery.Id;

    private string SurgeryDescription(EntProtoId surgery) =>
        _protos.TryIndex(surgery, out EntityPrototype? proto) ? proto.Description : string.Empty;

    private string StepName(string step) =>
        _protos.TryIndex(step, out EntityPrototype? proto) ? proto.Name : step;
}
