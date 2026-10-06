using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Client._WF.UserInterface.Controls;
using Content.Client.Lobby;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Preferences;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._WF.CustomMarkings.UI;

/// <summary>
/// The pixel editor for one custom marking: four facings drawn over the character's body, then saved to the
/// player's library under a name and a placement.
/// </summary>
public sealed partial class CustomMarkingEditorWindow : DefaultWindow
{
    private enum Tool
    {
        Pencil,
        Eraser,
        Fill,
        Picker,
    }

    private const int CanvasScale = 12;
    private const int FacingScale = 2;

    private static readonly string[] FacingNames =
    {
        "wf-custom-marking-facing-south",
        "wf-custom-marking-facing-north",
        "wf-custom-marking-facing-east",
        "wf-custom-marking-facing-west",
    };

    [Dependency] private IEntityManager _entMan = default!;

    private readonly CustomMarkingSystem _system;
    private readonly LobbyUIController _lobby;
    private readonly HumanoidCharacterProfile? _profile;

    /// <summary>The library entry being changed, or null for a new marking.</summary>
    private readonly CustomMarkingEntry? _entry;

    private readonly CustomMarkingSketch _sketch;
    private readonly byte[] _opened;

    private readonly CustomMarkingCanvas _canvas;
    private readonly CustomMarkingCanvas[] _facings = new CustomMarkingCanvas[CustomMarkingRules.Facings];
    private readonly WolfgateColorPicker _colour;
    private readonly Slider _opacity;
    private readonly LineEdit _name;
    private readonly OptionButton _placement;
    private readonly RichTextLabel _placementHint;
    private readonly CheckBox _showBody;
    private readonly CheckBox _showClothes;
    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly Button _mirrorButton;
    private readonly Button _saveButton;
    private readonly Label _status;

    private readonly Dictionary<Tool, Button> _toolButtons = new();

    private Tool _tool = Tool.Pencil;
    private Vector2i? _last;
    private EntityUid? _body;
    private int _request = -1;

    /// <summary>Raised once the server has saved the marking: the entry as it was opened, if any, and as saved.</summary>
    public Action<CustomMarkingEntry?, CustomMarkingEntry>? OnSaved;

    /// <param name="profile">The character whose body is drawn under the art, without the marking being edited.</param>
    public CustomMarkingEditorWindow(CustomMarkingEntry? entry, CustomMarkingArt art, string name, HumanoidCharacterProfile? profile)
    {
        IoCManager.InjectDependencies(this);
        _system = _entMan.System<CustomMarkingSystem>();
        _lobby = UserInterfaceManager.GetUIController<LobbyUIController>();
        _entry = entry;
        _sketch = new CustomMarkingSketch(art);
        _opened = art.Pixels.AsSpan().ToArray();
        _profile = profile;

        Title = Loc.GetString(entry == null ? "wf-custom-marking-editor-title-new" : "wf-custom-marking-editor-title-edit");

        _canvas = new CustomMarkingCanvas(CanvasScale, true) { Art = _sketch.Art, ShowGrid = true };
        _canvas.Stroke += OnStroke;
        _canvas.StrokeEnded += EndStroke;

        // Tools
        var toolGroup = new ButtonGroup();
        var tools = Column(120);
        tools.AddChild(Heading("wf-custom-marking-editor-tools"));
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var button = new Button
            {
                Text = Loc.GetString($"wf-custom-marking-tool-{tool.ToString().ToLowerInvariant()}"),
                ToolTip = Loc.GetString($"wf-custom-marking-tool-{tool.ToString().ToLowerInvariant()}-tooltip"),
                ToggleMode = true,
                Group = toolGroup,
                Pressed = tool == _tool,
            };
            button.OnPressed += _ => _tool = tool;
            _toolButtons[tool] = button;
            tools.AddChild(button);
        }

        _undoButton = Command("wf-custom-marking-editor-undo", Undo);
        _redoButton = Command("wf-custom-marking-editor-redo", Redo);
        _mirrorButton = Command("wf-custom-marking-editor-mirror", () => Change(art => art.CopyFacing(Facing, Opposite(Facing), true)));
        tools.AddChild(new Control { MinHeight = 6 });
        tools.AddChild(_undoButton);
        tools.AddChild(_redoButton);
        tools.AddChild(new Control { MinHeight = 6 });
        tools.AddChild(Heading("wf-custom-marking-editor-facing-tools"));
        tools.AddChild(Command("wf-custom-marking-editor-flip", () => Change(art => art.CopyFacing(Facing, Facing, true))));
        tools.AddChild(_mirrorButton);
        tools.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center,
            Children =
            {
                Nudge("◀", -1, 0),
                Nudge("▲", 0, -1),
                Nudge("▼", 0, 1),
                Nudge("▶", 1, 0),
            },
        });
        tools.AddChild(Command("wf-custom-marking-editor-clear", () => Change(art => art.Clear(Facing))));

        // Facings and view
        var view = Column(150);
        view.AddChild(Heading("wf-custom-marking-editor-facings"));
        var facingGroup = new ButtonGroup();
        var facingGrid = new GridContainer { Columns = 2, HSeparationOverride = 4, VSeparationOverride = 4 };
        for (var i = 0; i < _facings.Length; i++)
        {
            var facing = i;
            _facings[i] = new CustomMarkingCanvas(FacingScale, false) { Art = _sketch.Art, Facing = i };
            var button = new ContainerButton
            {
                StyleClasses = { ContainerButton.StyleClassButton },
                ToggleMode = true,
                Group = facingGroup,
                Pressed = i == 0,
                ToolTip = Loc.GetString(FacingNames[i]),
                Children =
                {
                    new BoxContainer
                    {
                        Orientation = BoxContainer.LayoutOrientation.Vertical,
                        Children =
                        {
                            _facings[i],
                            new Label { Text = Loc.GetString(FacingNames[i]), HorizontalAlignment = HAlignment.Center },
                        },
                    },
                },
            };
            button.OnPressed += _ => SetFacing(facing);
            facingGrid.AddChild(button);
        }

        view.AddChild(facingGrid);
        view.AddChild(new Control { MinHeight = 6 });
        _showBody = new CheckBox { Text = Loc.GetString("wf-custom-marking-editor-show-body"), Pressed = true };
        _showBody.OnPressed += _ => UpdateBody();
        _showClothes = new CheckBox { Text = Loc.GetString("wf-custom-marking-editor-show-clothes") };
        _showClothes.OnPressed += _ => ReloadBody();
        var grid = new CheckBox { Text = Loc.GetString("wf-custom-marking-editor-show-grid"), Pressed = true };
        grid.OnPressed += _ => _canvas.ShowGrid = grid.Pressed;
        view.AddChild(_showBody);
        view.AddChild(_showClothes);
        view.AddChild(grid);

        // Colour
        _colour = new WolfgateColorPicker { Color = Color.Black };
        _colour.OnColorChanged += _ => DrawAgain();
        _opacity = new Slider { MinValue = 5, MaxValue = 100, Value = 100, MinWidth = 90, VerticalAlignment = VAlignment.Center };
        var colourRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Children =
            {
                _colour,
                new Label { Text = Loc.GetString("wf-custom-marking-editor-opacity"), VerticalAlignment = VAlignment.Center },
                _opacity,
            },
        };

        if (profile != null)
        {
            var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
            body.AddChild(Swatch(profile.Appearance.SkinColor, "wf-custom-marking-editor-colour-skin"));
            body.AddChild(Swatch(profile.Appearance.HairColor, "wf-custom-marking-editor-colour-hair"));
            body.AddChild(Swatch(profile.Appearance.FacialHairColor, "wf-custom-marking-editor-colour-facial-hair"));
            body.AddChild(Swatch(profile.Appearance.EyeColor, "wf-custom-marking-editor-colour-eyes"));
            colourRow.AddChild(new Label { Text = Loc.GetString("wf-custom-marking-editor-colour-body"), VerticalAlignment = VAlignment.Center });
            colourRow.AddChild(body);
        }

        // Name, placement and saving
        _name = new LineEdit
        {
            Text = name,
            MinWidth = 180,
            PlaceHolder = Loc.GetString("wf-custom-marking-default-name"),
            IsValid = text => text.Length <= CustomMarkingRules.MaxNameLength,
        };
        _placement = new OptionButton { MinWidth = 150 };
        foreach (var placement in Enum.GetValues<CustomMarkingPlacement>())
        {
            _placement.AddItem(Loc.GetString(PlacementName(placement)), (int) placement);
        }

        _placement.SelectId((int) (entry?.Placement ?? CustomMarkingPlacement.Skin));
        _placement.OnItemSelected += args =>
        {
            _placement.SelectId(args.Id);
            UpdatePlacement();
        };
        _placementHint = new RichTextLabel { HorizontalExpand = true };

        _status = new Label { ClipText = true, HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
        _saveButton = new Button { Text = Loc.GetString("wf-custom-marking-editor-save"), StyleClasses = { StyleWolfgate.StyleClassCreatorPrimary } };
        _saveButton.OnPressed += _ => Save();
        var cancel = new Button { Text = Loc.GetString("wf-custom-marking-editor-cancel") };
        cancel.OnPressed += _ => Close();

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 8,
            Children =
            {
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 10,
                    Children = { tools, _canvas, view },
                },
                colourRow,
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 6,
                    Children =
                    {
                        new Label { Text = Loc.GetString("wf-custom-marking-editor-name"), VerticalAlignment = VAlignment.Center },
                        _name,
                        new Label { Text = Loc.GetString("wf-custom-marking-editor-placement"), VerticalAlignment = VAlignment.Center },
                        _placement,
                    },
                },
                _placementHint,
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 6,
                    Children = { _status, cancel, _saveButton },
                },
            },
        });

        _system.SaveAnswered += OnSaveAnswered;
        OnClose += () =>
        {
            _system.SaveAnswered -= OnSaveAnswered;
            DeleteBody();
        };

        ReloadBody();
        UpdatePlacement();
        UpdateButtons();
    }

    private CustomMarkingArt Art => _sketch.Art;

    private int Facing => _canvas.Facing;

    private CustomMarkingPlacement Placement => (CustomMarkingPlacement) _placement.SelectedId;

    public static string PlacementName(CustomMarkingPlacement placement)
    {
        return $"wf-custom-marking-placement-{placement.ToString().ToLowerInvariant()}";
    }

    private static BoxContainer Column(float width)
    {
        return new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = width,
            SeparationOverride = 3,
        };
    }

    private static Label Heading(string loc)
    {
        return new Label { Text = Loc.GetString(loc), StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel } };
    }

    private static Button Command(string loc, Action act)
    {
        var button = new Button { Text = Loc.GetString(loc) };
        button.OnPressed += _ => act();
        return button;
    }

    private Button Nudge(string text, int dx, int dy)
    {
        var button = new Button { Text = text, ToolTip = Loc.GetString("wf-custom-marking-editor-nudge") };
        button.OnPressed += _ => Change(art => art.Shift(Facing, dx, dy));
        return button;
    }

    private Control Swatch(Color color, string tooltip)
    {
        var swatch = new ContainerButton
        {
            StyleClasses = { WolfgateColorPicker.StyleClassSwatch },
            ToolTip = Loc.GetString(tooltip),
            Children =
            {
                new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat(color.WithAlpha(1f)),
                    MinSize = new Vector2(20, 20),
                    Margin = new Thickness(2),
                },
            },
        };
        swatch.OnPressed += _ => _colour.Color = color.WithAlpha(1f);
        return swatch;
    }

    /// <summary>Goes back to the pencil once a colour is chosen, from the palette or off the art.</summary>
    private void DrawAgain()
    {
        if (_tool is not (Tool.Picker or Tool.Eraser))
            return;

        _tool = Tool.Pencil;
        _toolButtons[Tool.Pencil].Pressed = true;
    }

    private Rgba32 Ink()
    {
        return CustomMarkingArt.ToPixel(_colour.Color.WithAlpha(_opacity.Value / 100f));
    }

    private void SetFacing(int facing)
    {
        _canvas.Facing = facing;
        UpdateButtons();
    }

    /// <summary>The other side view, which is this one seen in a mirror.</summary>
    private static int Opposite(int facing)
    {
        return facing == CustomMarkingArt.East ? CustomMarkingArt.West : CustomMarkingArt.East;
    }

    private void OnStroke(Vector2i pixel, bool erase, bool start)
    {
        var tool = erase ? Tool.Eraser : _tool;
        var inFrame = CustomMarkingArt.InFrame(pixel.X, pixel.Y);

        if (tool == Tool.Picker)
        {
            if (start && inFrame && Art.GetPixel(Facing, pixel.X, pixel.Y) is { A: > 0 } picked)
            {
                _colour.Color = CustomMarkingArt.ToColor(picked).WithAlpha(1f);
                _opacity.Value = MathF.Max(_opacity.MinValue, picked.A * 100f / byte.MaxValue);
                DrawAgain();
            }

            return;
        }

        if (start)
        {
            _sketch.Begin();
            _last = null;
        }

        if (tool == Tool.Fill)
        {
            if (start)
                Art.Fill(Facing, pixel.X, pixel.Y, Ink());

            return;
        }

        // A fast drag skips pixels, so each move draws the line from the last one.
        _sketch.Line(Facing, _last ?? pixel, pixel, tool == Tool.Eraser ? default : Ink());
        _last = pixel;
    }

    private void EndStroke()
    {
        _last = null;
        _sketch.End();
        UpdateButtons();
    }

    /// <summary>Runs one edit as a single undo step.</summary>
    private void Change(Action<CustomMarkingArt> edit)
    {
        _sketch.Change(edit);
        UpdateButtons();
    }

    private void Undo()
    {
        _sketch.Undo();
        UpdateButtons();
    }

    private void Redo()
    {
        _sketch.Redo();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _undoButton.Disabled = !_sketch.CanUndo;
        _redoButton.Disabled = !_sketch.CanRedo;
        _mirrorButton.Disabled = Facing is CustomMarkingArt.South or CustomMarkingArt.North;
    }

    private void UpdatePlacement()
    {
        _canvas.Placement = Placement;
        _placementHint.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(PlacementName(Placement) + "-hint")));
    }

    /// <summary>Remakes the doll drawn under the art, dressed for the character's job or not.</summary>
    private void ReloadBody()
    {
        DeleteBody();
        if (_profile != null)
            _body = _lobby.LoadProfileEntity(_profile, null, _showClothes.Pressed);

        UpdateBody();
    }

    private void UpdateBody()
    {
        _canvas.Body = _showBody.Pressed ? _body : null;
    }

    private void DeleteBody()
    {
        _canvas.Body = null;
        if (_body is { } body)
            _entMan.DeleteEntity(body);

        _body = null;
    }

    private void Save()
    {
        if (_request >= 0)
            return;

        if (Art.IsBlank())
        {
            SetStatus("wf-custom-marking-error-blank", true);
            return;
        }

        // Unchanged art isn't sent again: the server keeps what the entry has.
        var changed = _entry == null || !Art.Pixels.AsSpan().SequenceEqual(_opened);
        _request = _system.Save(_entry?.Id ?? 0, CustomMarkingRules.CleanName(_name.Text), Placement, changed ? Art : null);
        _saveButton.Disabled = true;
        SetStatus("wf-custom-marking-editor-saving", false);
    }

    private void OnSaveAnswered(CustomMarkingSaveResultEvent ev)
    {
        if (ev.Request != _request)
            return;

        _request = -1;
        _saveButton.Disabled = false;
        if (ev.Entry is not { } saved)
        {
            SetStatus(ev.Error ?? "wf-custom-marking-error-failed", true);
            return;
        }

        _system.Remember(saved.Hash, Art);
        OnSaved?.Invoke(_entry, saved);
        Close();
    }

    private void SetStatus(string loc, bool error)
    {
        _status.Text = Loc.GetString(loc);
        if (error)
            _status.StyleClasses.Add("Danger");
        else
            _status.StyleClasses.Remove("Danger");
    }
}
