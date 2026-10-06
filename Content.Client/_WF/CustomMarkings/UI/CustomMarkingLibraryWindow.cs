using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Client.Stylesheets;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Preferences;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Utility;

namespace Content.Client._WF.CustomMarkings.UI;

/// <summary>
/// A player's library of custom markings, opened from the character creator: draw or import new ones, change or
/// remove old ones, and pick which the character being edited wears.
/// </summary>
public sealed partial class CustomMarkingLibraryWindow : DefaultWindow
{
    private const int ThumbnailScale = 2;

    private static readonly Color ThumbnailBackdrop = Color.FromHex("#2A2D33");

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IFileDialogManager _dialog = default!;
    [Dependency] private ILogManager _log = default!;

    private readonly CustomMarkingSystem _system;
    private readonly Func<HumanoidCharacterProfile?> _profile;
    private readonly BoxContainer _rows;
    private readonly Label _counts;
    private readonly Label _status;
    private readonly Button _newButton;
    private readonly Button _importButton;

    private List<CustomMarking> _worn = new();
    private CustomMarkingEditorWindow? _editor;

    /// <summary>The entry whose Delete was pressed once and waits for a second press.</summary>
    private int _confirmingDelete;

    private bool _busy;

    /// <summary>Raised with the new list whenever the character's worn markings change here.</summary>
    public Action<List<CustomMarking>>? OnWornChanged;

    /// <param name="profile">The character being edited, as it is now.</param>
    public CustomMarkingLibraryWindow(Func<HumanoidCharacterProfile?> profile)
    {
        IoCManager.InjectDependencies(this);
        _system = _entMan.System<CustomMarkingSystem>();
        _profile = profile;

        Title = Loc.GetString("wf-custom-marking-library-title");
        MinSize = new Vector2(760, 520);

        var hint = new RichTextLabel { HorizontalExpand = true };
        hint.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("wf-custom-marking-library-hint")));

        _newButton = new Button { Text = Loc.GetString("wf-custom-marking-library-new"), StyleClasses = { StyleWolfgate.StyleClassCreatorPrimary } };
        _newButton.OnPressed += _ => OpenEditor(null, new CustomMarkingArt(), string.Empty);
        _importButton = new Button { Text = Loc.GetString("wf-custom-marking-library-import"), ToolTip = Loc.GetString("wf-custom-marking-library-import-tooltip") };
        _importButton.OnPressed += _ => Import();
        _counts = new Label { HorizontalExpand = true, Align = Label.AlignMode.Right, VerticalAlignment = VAlignment.Center };

        _rows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };

        _status = new Label { ClipText = true };

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
            Children =
            {
                hint,
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 6,
                    Children = { _newButton, _importButton, _counts },
                },
                new ScrollContainer
                {
                    VerticalExpand = true,
                    HorizontalExpand = true,
                    HScrollEnabled = false,
                    Children = { _rows },
                },
                _status,
            },
        });

        _system.LibraryUpdated += Rebuild;
        _system.ArtLoaded += OnArtLoaded;
        OnClose += () =>
        {
            _system.LibraryUpdated -= Rebuild;
            _system.ArtLoaded -= OnArtLoaded;
            _editor?.Close();
        };

        _system.RequestLibrary();
        Rebuild();
    }

    private int MaxWorn => Math.Min(_cfg.GetCVar(CustomMarkingCVars.MaxWorn), CustomMarkingRules.MaxWornCap);

    /// <summary>Shows which markings the character wears. Call when the profile is loaded or changed elsewhere.</summary>
    public void SetWorn(IEnumerable<CustomMarking> worn)
    {
        _worn = worn.ToList();
        Rebuild();
    }

    private void OnArtLoaded(string hash)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        _rows.RemoveAllChildren();

        var library = _system.Library;
        var limit = _cfg.GetCVar(CustomMarkingCVars.LibraryLimit);
        _counts.Text = Loc.GetString("wf-custom-marking-library-counts",
            ("saved", library?.Count ?? 0), ("limit", limit), ("worn", _worn.Count), ("max", MaxWorn));
        _newButton.Disabled = library == null || library.Count >= limit;
        _importButton.Disabled = _newButton.Disabled;

        if (library == null)
        {
            _rows.AddChild(new Label { Text = Loc.GetString("wf-custom-marking-library-loading"), StyleClasses = { "LabelSubText" } });
            return;
        }

        if (library.Count == 0 && _worn.Count == 0)
            _rows.AddChild(new Label { Text = Loc.GetString("wf-custom-marking-library-empty"), StyleClasses = { "LabelSubText" } });

        foreach (var entry in library)
        {
            _rows.AddChild(EntryRow(entry));
        }

        // Worn markings with no entry behind them: deleted from the library since, or from an imported character.
        foreach (var marking in _worn)
        {
            if (!library.Any(entry => entry.Hash == marking.Hash && entry.Placement == marking.Placement))
                _rows.AddChild(StrayRow(marking, library.Count < limit));
        }
    }

    private Control EntryRow(CustomMarkingEntry entry)
    {
        var marking = new CustomMarking(entry.Hash, entry.Placement);
        var worn = _worn.Contains(marking);
        var loaded = _system.TryGetArt(entry.Hash, out _);

        var wear = new Button
        {
            Text = Loc.GetString(worn ? "wf-custom-marking-library-take-off" : "wf-custom-marking-library-wear"),
            ToggleMode = true,
            Pressed = worn,
            Disabled = !worn && _worn.Count >= MaxWorn,
            MinWidth = 90,
        };
        wear.OnPressed += _ => SetWorn(marking, !worn);

        var edit = new Button { Text = Loc.GetString("wf-custom-marking-library-edit"), Disabled = !loaded };
        edit.OnPressed += _ =>
        {
            if (ReadArt(entry.Hash) is { } art)
                OpenEditor(entry, art, entry.Name);
        };

        var export = new Button { Text = Loc.GetString("wf-custom-marking-library-export"), Disabled = !loaded };
        export.OnPressed += _ => Export(entry.Hash);

        var confirming = _confirmingDelete == entry.Id;
        var delete = new Button
        {
            Text = Loc.GetString(confirming ? "wf-custom-marking-library-delete-confirm" : "wf-custom-marking-library-delete"),
            StyleClasses = { StyleBase.ButtonCaution },
        };
        delete.OnPressed += _ =>
        {
            if (confirming)
                _system.Delete(entry.Id);

            _confirmingDelete = confirming ? 0 : entry.Id;
            Rebuild();
        };

        return Row(entry.Hash, entry.Name, entry.Placement, wear, edit, export, delete);
    }

    private Control StrayRow(CustomMarking marking, bool room)
    {
        var takeOff = new Button { Text = Loc.GetString("wf-custom-marking-library-take-off"), MinWidth = 90 };
        takeOff.OnPressed += _ => SetWorn(marking, false);

        var keep = new Button
        {
            Text = Loc.GetString("wf-custom-marking-library-keep"),
            ToolTip = Loc.GetString("wf-custom-marking-library-keep-tooltip"),
            Disabled = !room || !_system.TryGetArt(marking.Hash, out _),
        };
        keep.OnPressed += _ =>
        {
            if (ReadArt(marking.Hash) is { } art)
                _system.Save(0, string.Empty, marking.Placement, art);
        };

        return Row(marking.Hash, Loc.GetString("wf-custom-marking-library-stray"), marking.Placement, takeOff, keep);
    }

    private Control Row(string hash, string name, CustomMarkingPlacement placement, params Control[] buttons)
    {
        var thumbnails = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
        RSI.State? state = null;
        if (_system.TryGetArt(hash, out var rsi))
            rsi.TryGetState(CustomMarkingResources.State, out state);

        for (var facing = 0; facing < CustomMarkingRules.Facings; facing++)
        {
            var size = CustomMarkingRules.FrameSize * ThumbnailScale;
            thumbnails.AddChild(new PanelContainer
            {
                PanelOverride = new StyleBoxFlat(ThumbnailBackdrop),
                MinSize = new Vector2(size, size),
                Children =
                {
                    new TextureRect
                    {
                        Texture = state?.GetFrame((RsiDirection) facing, 0),
                        TextureScale = new Vector2(ThumbnailScale, ThumbnailScale),
                    },
                },
            });
        }

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
            Children =
            {
                thumbnails,
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    VerticalAlignment = VAlignment.Center,
                    Children =
                    {
                        new Label { Text = name, ClipText = true },
                        new Label
                        {
                            Text = Loc.GetString(CustomMarkingEditorWindow.PlacementName(placement)),
                            StyleClasses = { "LabelSubText" },
                        },
                    },
                },
            },
        };

        foreach (var button in buttons)
        {
            button.VerticalAlignment = VAlignment.Center;
            row.AddChild(button);
        }

        return new PanelContainer
        {
            StyleClasses = { StyleWolfgate.StyleClassCreatorCard },
            HorizontalExpand = true,
            Children = { row },
        };
    }

    private void SetWorn(CustomMarking marking, bool worn)
    {
        if (worn == _worn.Contains(marking) || worn && _worn.Count >= MaxWorn)
            return;

        if (worn)
            _worn.Add(marking);
        else
            _worn.Remove(marking);

        WornChanged();
    }

    private void WornChanged()
    {
        SetStatus(null, false);
        OnWornChanged?.Invoke(_worn.ToList());
        Rebuild();
    }

    private CustomMarkingArt? ReadArt(string hash)
    {
        return _system.TryGetPng(hash, out var png) ? CustomMarkingPng.Read(png) : null;
    }

    private void OpenEditor(CustomMarkingEntry? entry, CustomMarkingArt art, string name)
    {
        _editor?.Close();

        // The body under the art wears everything else, but not the marking being changed.
        var body = _profile();
        if (body != null && entry is { } editing)
            body = body.WithCustomMarkings(_worn.Where(marking => marking.Hash != editing.Hash || marking.Placement != editing.Placement));

        var editor = new CustomMarkingEditorWindow(entry, art, name, body);
        editor.OnSaved += OnSaved;
        editor.OnClose += () =>
        {
            if (_editor == editor)
                _editor = null;
        };
        _editor = editor;
        editor.OpenCentered();
    }

    /// <summary>A changed marking stays on the character; a new one goes on if there is room.</summary>
    private void OnSaved(CustomMarkingEntry? before, CustomMarkingEntry saved)
    {
        if (CustomMarkingRules.ApplySaved(_worn, before, saved, MaxWorn))
            WornChanged();
    }

    private async void Import()
    {
        if (_busy)
            return;

        _busy = true;
        try
        {
            await using var file = await _dialog.OpenFile(new FileDialogFilters(new FileDialogFilters.Group("png")));
            if (file == null || Disposed)
                return;

            var art = file.Length <= CustomMarkingPng.MaxFileBytes ? CustomMarkingPng.Read(file.CopyToArray()) : null;
            if (art == null)
            {
                SetStatus("wf-custom-marking-error-import", true);
                return;
            }

            SetStatus(null, false);
            OpenEditor(null, art, string.Empty);
        }
        catch (Exception e)
        {
            _log.GetSawmill("wf.custom_markings").Error($"Importing a custom marking failed: {e}");
            SetStatus("wf-custom-marking-error-import", true);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void Export(string hash)
    {
        if (_busy || !_system.TryGetPng(hash, out var png))
            return;

        _busy = true;
        try
        {
            var file = await _dialog.SaveFile(new FileDialogFilters(new FileDialogFilters.Group("png")));
            if (file is not { } saved)
                return;

            await using var stream = saved.fileStream;
            // A file picked to be replaced still holds its old bytes.
            if (saved.alreadyExisted && stream.CanSeek)
                stream.SetLength(0);

            await stream.WriteAsync(png);
        }
        catch (Exception e)
        {
            _log.GetSawmill("wf.custom_markings").Error($"Exporting a custom marking failed: {e}");
            SetStatus("wf-custom-marking-error-export", true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetStatus(string? loc, bool error)
    {
        _status.Text = loc == null ? string.Empty : Loc.GetString(loc);
        if (error)
            _status.StyleClasses.Add("Danger");
        else
            _status.StyleClasses.Remove("Danger");
    }
}
