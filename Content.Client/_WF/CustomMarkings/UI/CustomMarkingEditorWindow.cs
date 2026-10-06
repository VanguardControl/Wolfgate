using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Client._WF.UserInterface.Controls;
using Content.Client.Lobby;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._WF.CustomMarkings.UI;

/// <summary>
/// The pixel editor for one custom marking: four facings drawn over the character's body, then saved to the
/// player's library under a name and a placement.
/// </summary>
public sealed partial class CustomMarkingEditorWindow : CustomMarkingWindow
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

    /// <summary>
    /// Width of the window's content, which the placement hint wraps to. It has to hold the tools, the canvas and
    /// the facing panel side by side: about 760 with the longest facing names.
    /// </summary>
    private const float ContentWidth = 790;

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
    private readonly CustomMarkingBodySampler _sampler;
    private readonly Label _canvasHeading;
    private readonly CustomMarkingCanvas[] _facings = new CustomMarkingCanvas[CustomMarkingRules.Facings];
    private readonly WolfgateColorPicker _colour;
    private readonly Slider _opacity;
    private readonly LineEdit _name;
    private readonly OptionButton _placement;
    private readonly RichTextLabel _placementHint;
    private readonly CheckBox _showBody;
    private readonly CheckBox _showClothes;
    private readonly CustomMarkingIconButton _undoButton;
    private readonly CustomMarkingIconButton _redoButton;
    private readonly CustomMarkingIconButton _mirrorButton;
    private readonly Button _saveButton;
    private readonly Label _status;

    private readonly Dictionary<Tool, CustomMarkingIconButton> _toolButtons = new();

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
        Resizable = false;

        // The canvas, and the body sampler the colour picker reads from
        _canvas = new CustomMarkingCanvas(CanvasScale, true) { Art = _sketch.Art, ShowGrid = true };
        _canvas.Stroke += OnStroke;
        _canvas.StrokeEnded += EndStroke;
        _canvasHeading = Heading(FacingNames[0]);
        _canvasHeading.HorizontalExpand = true;
        _canvasHeading.VAlign = Label.VAlignMode.Center;
        _sampler = new CustomMarkingBodySampler();
        UserInterfaceManager.RootControl.AddChild(_sampler);

        // Drawing tools, in a block two wide down the left of the canvas
        var toolGroup = new ButtonGroup();
        var drawing = ToolGrid();
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var icon = tool.ToString().ToLowerInvariant();
            var button = new CustomMarkingIconButton(icon, Loc.GetString($"wf-custom-marking-tool-{icon}"))
            {
                ToggleMode = true,
                Group = toolGroup,
                Pressed = tool == _tool,
            };
            button.AddStyleClass(StyleWolfgate.StyleClassCreatorToggle);
            button.OnPressed += _ => SetTool(tool);
            _toolButtons[tool] = button;
            drawing.AddChild(button);
        }

        var symmetry = new CustomMarkingIconButton("symmetry", Loc.GetString("wf-custom-marking-editor-symmetry")) { ToggleMode = true };
        symmetry.AddStyleClass(StyleWolfgate.StyleClassCreatorToggle);
        symmetry.OnToggled += args => SetMirror(args.Pressed);
        drawing.AddChild(symmetry);

        _undoButton = Command("undo", "wf-custom-marking-editor-undo", Undo);
        _redoButton = Command("redo", "wf-custom-marking-editor-redo", Redo);
        var history = ToolGrid();
        history.AddChild(_undoButton);
        history.AddChild(_redoButton);

        // What can be done to the facing shown
        _mirrorButton = Command("mirror", "wf-custom-marking-editor-mirror", () => Change(art => art.CopyFacing(Facing, Opposite(Facing), true)));
        var facingTools = ToolGrid();
        facingTools.AddChild(Nudge("left", -1, 0));
        facingTools.AddChild(Nudge("right", 1, 0));
        facingTools.AddChild(Nudge("up", 0, -1));
        facingTools.AddChild(Nudge("down", 0, 1));
        facingTools.AddChild(Command("flip", "wf-custom-marking-editor-flip", () => Change(art => art.Flip(Facing, _sketch.MirrorAxis))));
        facingTools.AddChild(_mirrorButton);
        facingTools.AddChild(Command("trash", "wf-custom-marking-editor-clear", () => Change(art => art.Clear(Facing))));

        var tools = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 10,
            Children = { drawing, history, facingTools },
        };

        // The four facings, each on the body, and what the canvas shows
        var facingGroup = new ButtonGroup();
        var facingGrid = new GridContainer { Columns = 2, HSeparationOverride = 4, VSeparationOverride = 4 };
        for (var i = 0; i < _facings.Length; i++)
        {
            var facing = i;
            _facings[i] = new CustomMarkingCanvas(FacingScale, false) { Art = _sketch.Art, Facing = i };
            var tile = new ContainerButton
            {
                StyleClasses = { ContainerButton.StyleClassButton, StyleWolfgate.StyleClassCreatorToggle },
                ToggleMode = true,
                Group = facingGroup,
                Pressed = i == 0,
                ToolTip = Loc.GetString(FacingNames[i]),
                Children =
                {
                    new BoxContainer
                    {
                        Orientation = BoxContainer.LayoutOrientation.Vertical,
                        SeparationOverride = 2,
                        Children =
                        {
                            _facings[i],
                            new Label
                            {
                                Text = Loc.GetString(FacingNames[i]),
                                Align = Label.AlignMode.Center,
                                StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
                            },
                        },
                    },
                },
            };
            tile.OnPressed += _ => SetFacing(facing);
            facingGrid.AddChild(tile);
        }

        _showBody = new CheckBox { Text = Loc.GetString("wf-custom-marking-editor-show-body"), Pressed = true };
        _showBody.OnPressed += _ => UpdateBody();
        _showClothes = new CheckBox { Text = Loc.GetString("wf-custom-marking-editor-show-clothes") };
        _showClothes.OnPressed += _ => ReloadBody();
        var showGrid = new CheckBox { Text = Loc.GetString("wf-custom-marking-editor-show-grid"), Pressed = true };
        showGrid.OnPressed += _ => _canvas.ShowGrid = showGrid.Pressed;

        var view = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Children =
            {
                Heading("wf-custom-marking-editor-facings"),
                facingGrid,
                new Control { MinHeight = 8 },
                _showBody,
                _showClothes,
                showGrid,
            },
        };

        // Colour: the palette, the character's own colours beside it, and the opacity
        _colour = new WolfgateColorPicker { Color = Color.Black, VerticalAlignment = VAlignment.Center };
        _colour.OnColorChanged += _ => DrawAgain();
        _opacity = new Slider { MinValue = 5, MaxValue = 100, Value = 100, MinWidth = 120, VerticalAlignment = VAlignment.Center };
        var colours = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            Children = { _colour },
        };
        if (profile != null)
        {
            colours.AddChild(new GridContainer
            {
                Columns = 2,
                HSeparationOverride = 2,
                VSeparationOverride = 2,
                VerticalAlignment = VAlignment.Center,
                Children =
                {
                    Swatch(profile.Appearance.SkinColor, "wf-custom-marking-editor-colour-skin"),
                    Swatch(profile.Appearance.HairColor, "wf-custom-marking-editor-colour-hair"),
                    Swatch(profile.Appearance.FacialHairColor, "wf-custom-marking-editor-colour-facial-hair"),
                    Swatch(profile.Appearance.EyeColor, "wf-custom-marking-editor-colour-eyes"),
                },
            });
        }

        colours.AddChild(new Control { HorizontalExpand = true });
        colours.AddChild(FieldLabel("wf-custom-marking-editor-opacity"));
        colours.AddChild(_opacity);

        // Name and placement, with what the placement means under them
        _name = new LineEdit
        {
            Text = name,
            MinWidth = 180,
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("wf-custom-marking-default-name"),
            IsValid = text => text.Length <= CustomMarkingRules.MaxNameLength,
        };
        _placement = new OptionButton { MinWidth = 170 };
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
            SeparationOverride = 8,
            SetWidth = ContentWidth,
            Children =
            {
                Card(new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    SeparationOverride = 6,
                    Children =
                    {
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 6,
                            Children =
                            {
                                FieldLabel("wf-custom-marking-editor-name"),
                                _name,
                                new Control { MinWidth = 6 },
                                FieldLabel("wf-custom-marking-editor-placement"),
                                _placement,
                            },
                        },
                        _placementHint,
                    },
                }, true),
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 8,
                    Children =
                    {
                        Card(new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 8,
                            Children =
                            {
                                tools,
                                new BoxContainer
                                {
                                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                                    SeparationOverride = 6,
                                    Children = { _canvasHeading, _canvas },
                                },
                            },
                        }, false),
                        Card(view, true),
                    },
                },
                Card(colours, true),
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
            _sampler.Orphan();
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

    private static Label Heading(string loc)
    {
        return new Label { Text = Loc.GetString(loc), StyleClasses = { StyleWolfgate.StyleClassCreatorHeading } };
    }

    private static Label FieldLabel(string loc)
    {
        return new Label
        {
            Text = Loc.GetString(loc),
            VerticalAlignment = VAlignment.Center,
            StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
        };
    }

    /// <summary>Groups editor controls using the character creator's inset panels.</summary>
    private static PanelContainer Card(Control content, bool expand)
    {
        return new PanelContainer
        {
            StyleClasses = { StyleWolfgate.StyleClassCreatorGroup },
            HorizontalExpand = expand,
            Children = { content },
        };
    }

    /// <summary>A block of icon buttons, two to a row.</summary>
    private static GridContainer ToolGrid()
    {
        return new GridContainer { Columns = 2, HSeparationOverride = 4, VSeparationOverride = 4 };
    }

    private static CustomMarkingIconButton Command(string icon, string loc, Action act)
    {
        var button = new CustomMarkingIconButton(icon, Loc.GetString(loc));
        button.OnPressed += _ => act();
        return button;
    }

    private CustomMarkingIconButton Nudge(string direction, int dx, int dy)
    {
        return Command(direction, $"wf-custom-marking-editor-nudge-{direction}", () => Change(art => art.Shift(Facing, dx, dy)));
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

    /// <summary>Turns mirror drawing on or off, with a line on the canvas where the mirror stands.</summary>
    private void SetMirror(bool on)
    {
        _sketch.Mirror = on;
        UpdateMirror();
    }

    /// <summary>Stands the mirror on the middle of the body in the facing shown, read off its torso.</summary>
    private void UpdateMirror()
    {
        var axis = CustomMarkingSketch.DefaultMirrorAxis;
        if (_body is { } body
            && _entMan.TryGetComponent(body, out SpriteComponent? sprite)
            && _system.MirrorAxis((body, sprite), Facing) is { } middle)
            axis = middle;

        _sketch.MirrorAxis = axis;
        _canvas.MirrorAxis = _sketch.Mirror ? axis : null;
    }

    private void SetTool(Tool tool)
    {
        _tool = tool;
        _sampler.Active = tool == Tool.Picker;
    }

    /// <summary>Goes back to the pencil once a colour is chosen, from the palette or off the canvas.</summary>
    private void DrawAgain()
    {
        if (_tool is not (Tool.Picker or Tool.Eraser))
            return;

        SetTool(Tool.Pencil);
        _toolButtons[Tool.Pencil].Pressed = true;
    }

    private Rgba32 Ink()
    {
        return CustomMarkingArt.ToPixel(_colour.Color.WithAlpha(_opacity.Value / 100f));
    }

    private void SetFacing(int facing)
    {
        _canvas.Facing = facing;
        _sampler.Facing = facing;
        _canvasHeading.Text = Loc.GetString(FacingNames[facing]);
        UpdateMirror();
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
            if (start && inFrame)
                Pick(pixel);

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
                _sketch.Fill(Facing, pixel.X, pixel.Y, Ink());

            return;
        }

        // A fast drag skips pixels, so each move draws the line from the last one.
        _sketch.Line(Facing, _last ?? pixel, pixel, tool == Tool.Eraser ? default : Ink());
        _last = pixel;
    }

    /// <summary>Takes the colour at a pixel: what is drawn there, or else the body showing through.</summary>
    private void Pick(Vector2i pixel)
    {
        if (Art.GetPixel(Facing, pixel.X, pixel.Y) is { A: > 0 } drawn)
        {
            _colour.Color = CustomMarkingArt.ToColor(drawn).WithAlpha(1f);
            _opacity.Value = MathF.Max(_opacity.MinValue, drawn.A * 100f / byte.MaxValue);
        }
        else if (_sampler.TryGetColor(pixel.X, pixel.Y, out var body))
        {
            _colour.Color = body;
            _opacity.Value = _opacity.MaxValue;
        }
        else
        {
            return;
        }

        DrawAgain();
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
        foreach (var tile in _facings)
        {
            tile.Placement = Placement;
        }

        _placementHint.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(PlacementName(Placement) + "-hint")));
    }

    /// <summary>Remakes the doll drawn under the art, dressed for the character's job or not.</summary>
    private void ReloadBody()
    {
        DeleteBody();
        if (_profile != null)
            _body = _lobby.LoadProfileEntity(_profile, null, _showClothes.Pressed);

        UpdateBody();
        UpdateMirror();
    }

    private void UpdateBody()
    {
        var shown = _showBody.Pressed ? _body : null;
        _canvas.Body = shown;
        _sampler.Body = shown;
        foreach (var tile in _facings)
        {
            tile.Body = shown;
        }
    }

    private void DeleteBody()
    {
        var body = _body;
        _body = null;
        UpdateBody();
        if (body is { } doll)
            _entMan.DeleteEntity(doll);
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
