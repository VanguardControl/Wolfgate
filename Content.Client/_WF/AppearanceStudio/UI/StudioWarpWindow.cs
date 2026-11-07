using System.Numerics;
using Content.Client._WF.CustomMarkings.UI;
using Content.Client._WF.Stylesheets;
using Content.Shared._WF.AppearanceStudio;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.AppearanceStudio.UI;

/// <summary>
/// Paints the warp (displacement map) of one worn or held item: push the item's pixels into shape over the body,
/// smooth, restore or cut them, per facing. Saving stores the warp on the server and hands back its hash.
/// </summary>
public sealed class StudioWarpWindow : CustomMarkingWindow
{
    private const int UndoDepth = 32;

    private readonly StudioWarpSystem _warps;
    private readonly StudioWarpCanvas _canvas;
    private readonly Label _status;
    private readonly Button _save;
    private readonly List<Button> _facingButtons = new();
    private readonly List<Button> _toolButtons = new();
    private readonly List<StudioWarp> _undo = new();
    private readonly Func<int, Vector2i> _offsetFor;

    private int? _request;

    /// <summary>The stored warp's hash, or null to remove the warp.</summary>
    public event Action<string?>? Saved;

    /// <summary>The warp as painted so far, each time a stroke ends, to show on the doll.</summary>
    public event Action<StudioWarp>? Previewed;

    public StudioWarpWindow(
        string partName,
        Rgba32[][]? frames,
        StudioWarp? warp,
        int facing,
        SpriteComponent body,
        HashSet<int> itemLayers,
        Func<int, Vector2i> offsetFor)
    {
        _warps = IoCManager.Resolve<IEntityManager>().System<StudioWarpSystem>();
        _offsetFor = offsetFor;
        Title = Loc.GetString("wf-studio-warp-title", ("part", partName));
        MinSize = new Vector2(760, 560);

        _canvas = new StudioWarpCanvas { Body = body, ItemLayers = itemLayers };
        _canvas.SetFrames(frames);
        _canvas.SetWarp(warp ?? new StudioWarp());
        _canvas.StrokeStarted += PushUndo;
        _canvas.StrokeEnded += () => Previewed?.Invoke(_canvas.Warp);

        _status = new Label { StyleClasses = { "LabelSubText" }, HorizontalExpand = true };
        _save = new Button { Text = Loc.GetString("wf-studio-warp-save"), StyleClasses = { StyleWolfgate.StyleClassCreatorPrimary } };
        _save.OnPressed += _ => Save();

        var tools = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, MinWidth = 240 };
        tools.AddChild(Heading("wf-studio-warp-tools"));
        var toolRow = new GridContainer { Columns = 2 };
        foreach (var tool in Enum.GetValues<StudioWarpTool>())
        {
            var button = new Button
            {
                Text = Loc.GetString($"wf-studio-warp-tool-{tool.ToString().ToLowerInvariant()}"),
                ToolTip = Loc.GetString($"wf-studio-warp-tool-{tool.ToString().ToLowerInvariant()}-tooltip"),
                ToggleMode = true,
                HorizontalExpand = true,
            };
            button.OnPressed += _ => SetTool(tool);
            _toolButtons.Add(button);
            toolRow.AddChild(button);
        }

        tools.AddChild(toolRow);
        tools.AddChild(SliderRow("wf-studio-warp-radius", 1f, 8f, _canvas.Radius, v => _canvas.Radius = v));
        tools.AddChild(SliderRow("wf-studio-warp-strength", 0.1f, 1f, _canvas.Strength, v => _canvas.Strength = v));

        tools.AddChild(Heading("wf-studio-facing"));
        var facings = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
        for (var i = 0; i < StudioRules.Facings; i++)
        {
            var index = i;
            var button = new Button { Text = Loc.GetString($"wf-studio-facing-{index}"), ToggleMode = true, HorizontalExpand = true };
            button.OnPressed += _ => SetFacing(index);
            _facingButtons.Add(button);
            facings.AddChild(button);
        }

        tools.AddChild(facings);
        tools.AddChild(ActionButton("wf-studio-warp-copy-all", () =>
        {
            for (var i = 0; i < StudioRules.Facings; i++)
            {
                _canvas.Warp.CopyFacing(_canvas.Facing, i, false);
            }
        }));
        tools.AddChild(ActionButton("wf-studio-warp-mirror", () =>
        {
            var other = _canvas.Facing switch { 2 => 3, 3 => 2, _ => -1 };
            if (other >= 0)
                _canvas.Warp.CopyFacing(_canvas.Facing, other, true);
        }));
        tools.AddChild(ActionButton("wf-studio-warp-clear", () => _canvas.Warp.ClearFacing(_canvas.Facing)));
        tools.AddChild(ActionButton("wf-studio-warp-undo", Undo, false));

        var original = new CheckBox { Text = Loc.GetString("wf-studio-warp-show-original") };
        original.OnToggled += args =>
        {
            _canvas.ShowOriginal = args.Pressed;
            _canvas.WarpChanged();
        };
        tools.AddChild(original);

        tools.AddChild(StudioText.Wrapped(Loc.GetString(frames == null ? "wf-studio-warp-no-frames" : "wf-studio-warp-help"), "LabelSubText", 240));

        var remove = new Button { Text = Loc.GetString("wf-studio-warp-remove"), Disabled = warp == null };
        remove.OnPressed += _ =>
        {
            Saved?.Invoke(null);
            Close();
        };

        var footer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        footer.AddChild(_status);
        footer.AddChild(remove);
        footer.AddChild(_save);

        var body2 = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 10, VerticalExpand = true };
        body2.AddChild(_canvas);
        body2.AddChild(tools);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(8) };
        root.AddChild(body2);
        root.AddChild(footer);
        Contents.AddChild(root);

        SetTool(StudioWarpTool.Push);
        SetFacing(facing);
        _warps.SaveAnswered += OnSaveAnswered;
        OnClose += () => _warps.SaveAnswered -= OnSaveAnswered;
    }

    /// <summary>The doll's layers the item draws, after the doll was redrawn and they moved.</summary>
    public void SetItemLayers(HashSet<int> layers)
    {
        _canvas.ItemLayers = layers;
    }

    private static Label Heading(string loc)
    {
        return new Label { Text = Loc.GetString(loc), StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel } };
    }

    private Button ActionButton(string loc, Action act, bool undoable = true)
    {
        var button = new Button { Text = Loc.GetString(loc), HorizontalExpand = true };
        button.OnPressed += _ =>
        {
            if (undoable)
                PushUndo();

            act();
            _canvas.WarpChanged();
            Previewed?.Invoke(_canvas.Warp);
        };
        return button;
    }

    private static Control SliderRow(string loc, float min, float max, float value, Action<float> changed)
    {
        var label = new Label { Text = Loc.GetString(loc, ("value", value.ToString("0.#"))) };
        var slider = new Slider { MinValue = min, MaxValue = max, Value = value, HorizontalExpand = true };
        slider.OnValueChanged += range =>
        {
            changed(range.Value);
            label.Text = Loc.GetString(loc, ("value", range.Value.ToString("0.#")));
        };

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        row.AddChild(label);
        row.AddChild(slider);
        return row;
    }

    private void SetTool(StudioWarpTool tool)
    {
        _canvas.Tool = tool;
        for (var i = 0; i < _toolButtons.Count; i++)
        {
            _toolButtons[i].Pressed = i == (int) tool;
        }
    }

    private void SetFacing(int facing)
    {
        _canvas.SetFacing(facing);
        _canvas.ItemOffset = _offsetFor(facing);
        for (var i = 0; i < _facingButtons.Count; i++)
        {
            _facingButtons[i].Pressed = i == facing;
        }
    }

    private void PushUndo()
    {
        _undo.Add(_canvas.Warp.Clone());
        if (_undo.Count > UndoDepth)
            _undo.RemoveAt(0);
    }

    private void Undo()
    {
        if (_undo.Count == 0)
            return;

        _canvas.SetWarp(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    private void Save()
    {
        if (_canvas.Warp.IsNeutral())
        {
            Saved?.Invoke(null);
            Close();
            return;
        }

        _save.Disabled = true;
        _status.Text = Loc.GetString("wf-studio-warp-saving");
        _request = _warps.Save(_canvas.Warp);
    }

    private void OnSaveAnswered(StudioWarpSaveResultEvent ev)
    {
        if (ev.Request != _request)
            return;

        _request = null;
        _save.Disabled = false;
        if (ev.Hash == null)
        {
            _status.Text = Loc.GetString(ev.Error ?? "wf-studio-error-failed");
            return;
        }

        Saved?.Invoke(ev.Hash);
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _warps.SaveAnswered -= OnSaveAnswered;
    }
}
