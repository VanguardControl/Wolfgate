using System.IO;
using System.Numerics;
using Content.Client._WF.MappingMode.Modes;
using Content.Client._WF.MappingMode.Screen;
using Content.Client._WF.MappingTools;
using Content.Client.Popups;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._WF.MappingMode;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.MappingMode.Template;

/// <summary>
/// The mapping screen's PNG template: one image at a time, loaded through the file dialog, drawn by
/// <see cref="WFMappingTemplateOverlay"/> and moved by <see cref="WFMappingTemplateMode"/>. The image and its settings
/// are kept per session name in the client data folder and come back when a session of that name is entered.
/// </summary>
public sealed partial class WFMappingTemplateSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IFileDialogManager _dialogs = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private MappingZLevelsSystem _zLevels = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private WFMappingBuildModeSystem _modes = default!;
    [Dependency] private WFMappingClientSystem _client = default!;

    private static readonly ResPath Root = new("/Mapping/Templates");
    private const string ImageFile = "template.png";
    private const string SettingsFile = "settings.yml";
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(1);

    private readonly List<int> _depths = new();
    private WFMappingTemplateMode _mode = default!;
    private string? _sessionName;
    private ResPath? _folder;
    private bool _dirty;
    private TimeSpan _nextSave;
    private bool _picking;
    private WFMappingTemplatePanel? _panel;
    private WFMappingDockSlot? _slot;

    /// <summary>The decoded image, or null with no template.</summary>
    public Texture? Texture { get; private set; }

    /// <summary>Where and how the image is drawn; call <see cref="Modified"/> after changing it.</summary>
    public WFMappingTemplateSettings Settings { get; } = new();

    public bool HasTemplate => Texture != null;

    /// <summary>Whether a session is entered, so a template can be loaded.</summary>
    public bool CanLoad => _folder != null && !_picking;

    /// <summary>Raised when the image or a setting changes.</summary>
    public event Action? Changed;

    public override void Initialize()
    {
        base.Initialize();

        _mode = new WFMappingTemplateMode(_modes, this);
        _modes.RegisterHandler(_mode);
        _client.SessionEnded += OnSessionEnded;

        var gameplay = _ui.GetUIController<GameplayStateLoadController>();
        gameplay.OnScreenLoad += OnScreenLoad;
        gameplay.OnScreenUnload += OnScreenUnload;

        CommandBinds.Builder
            .Bind(WFMappingKeyFunctions.WFMappingTemplateNudgeUp, Nudge(0, 1))
            .Bind(WFMappingKeyFunctions.WFMappingTemplateNudgeDown, Nudge(0, -1))
            .Bind(WFMappingKeyFunctions.WFMappingTemplateNudgeLeft, Nudge(-1, 0))
            .Bind(WFMappingKeyFunctions.WFMappingTemplateNudgeRight, Nudge(1, 0))
            .Register<WFMappingTemplateSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<WFMappingTemplateSystem>();
        _client.SessionEnded -= OnSessionEnded;
        var gameplay = _ui.GetUIController<GameplayStateLoadController>();
        gameplay.OnScreenLoad -= OnScreenLoad;
        gameplay.OnScreenUnload -= OnScreenUnload;
        OnScreenUnload();
        LeaveSession();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Entering a session, or its name changing, switches to that name's template.
        if (_client.SetUp && _client.Session is { } session && session.Name != _sessionName)
        {
            LeaveSession();
            _sessionName = session.Name;
            _folder = Root / FolderKey(session.Name);
            Restore();
        }

        if (_mode.Dragging)
            _mode.UpdateDrag();

        if (_dirty && _timing.RealTime >= _nextSave)
            SaveSettings();
    }

    /// <summary>Marks the settings changed: redraws the panel and saves within a second.</summary>
    public void Modified()
    {
        _dirty = true;
        Changed?.Invoke();
    }

    /// <summary>A session's folder name: the name itself when it is a valid library name, else its UTF-8 hex.</summary>
    public static string FolderKey(string sessionName)
    {
        var trimmed = sessionName.Trim();
        return WFMappingModeLimits.IsValidName(trimmed)
            ? trimmed
            : Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(sessionName));
    }

    /// <summary>The map position at the centre of the view, rounded to whole tiles.</summary>
    public Vector2 ViewCentre()
    {
        var centre = _eye.CurrentEye.Position.Position;
        return new Vector2(MathF.Round(centre.X), MathF.Round(centre.Y));
    }

    /// <summary>Opens the file dialog and loads the picked PNG as the template, replacing the old one.</summary>
    public async void Load()
    {
        if (_picking || _folder is not { } folder)
            return;

        _picking = true;
        Changed?.Invoke();
        try
        {
            var filters = new FileDialogFilters(new FileDialogFilters.Group("png"));
            var (stream, _) = await _dialogs.GetFileAndName(filters, FileAccess.Read);
            if (stream == null)
                return;

            byte[]? data;
            await using (stream)
            {
                data = ReadCapped(stream, WFMappingPngHeader.MaxFileBytes);
            }

            // Left the session, or entered another, while the dialog was open.
            if (_folder != folder)
                return;

            if (data == null)
            {
                Popup("wf-mapping-template-file-too-large", ("max", WFMappingPngHeader.MaxFileBytes / (1024 * 1024)));
                return;
            }

            if (!CheckPng(data) || Decode(data, true) is not { } texture)
                return;

            SetTexture(texture);
            _zLevels.GetStack(_depths, out var depth);
            Settings.Reset(ViewCentre());
            Settings.Depth = depth;
            WriteImage(folder, data);
            SaveSettings();
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error($"Loading a mapping template failed: {e}");
            Popup("wf-mapping-template-read-failed");
        }
        finally
        {
            _picking = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Drops the template and deletes its saved copy.</summary>
    public void Clear()
    {
        _mode.CancelDrag();
        SetTexture(null);
        Settings.Reset(Vector2.Zero);
        _dirty = false;
        if (_folder is { } folder)
        {
            try
            {
                foreach (var file in new[] { folder / ImageFile, folder / SettingsFile })
                {
                    if (_resources.UserData.Exists(file))
                        _resources.UserData.Delete(file);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Deleting the mapping template in {folder} failed: {e.Message}");
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Back to the defaults, centred on the view.</summary>
    public void Reset()
    {
        _mode.CancelDrag();
        Settings.Reset(ViewCentre());
        Modified();
    }

    public void Popup(string locKey, params (string, object)[] args)
    {
        _popup.PopupCursor(Loc.GetString(locKey, args));
    }

    private void OnSessionEnded()
    {
        LeaveSession();
    }

    /// <summary>Saves pending settings and forgets the template, keeping its files.</summary>
    private void LeaveSession()
    {
        if (_dirty)
            SaveSettings();

        _mode.CancelDrag();
        SetTexture(null);
        Settings.Reset(Vector2.Zero);
        _sessionName = null;
        _folder = null;
        Changed?.Invoke();
    }

    /// <summary>Loads the session's saved template, if it has one.</summary>
    private void Restore()
    {
        if (_folder is not { } folder)
            return;

        var image = folder / ImageFile;
        var settings = folder / SettingsFile;
        try
        {
            if (!_resources.UserData.Exists(image))
                return;

            var data = _resources.UserData.ReadAllBytes(image);
            if (WFMappingPngHeader.Check(data, out _, out _) != WFMappingPngCheck.Ok ||
                Decode(data, false) is not { } texture)
            {
                return;
            }

            SetTexture(texture);
            if (_resources.UserData.Exists(settings) && _resources.UserData.TryReadAllText(settings, out var text))
                Settings.CopyFrom(WFMappingTemplateSettings.Read(text));
            else
                Settings.Reset(ViewCentre());
        }
        catch (Exception e)
        {
            Log.Warning($"Reading the mapping template in {folder} failed: {e.Message}");
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    private bool CheckPng(byte[] data)
    {
        switch (WFMappingPngHeader.Check(data, out _, out _))
        {
            case WFMappingPngCheck.Ok:
                return true;
            case WFMappingPngCheck.FileTooLarge:
                Popup("wf-mapping-template-file-too-large", ("max", WFMappingPngHeader.MaxFileBytes / (1024 * 1024)));
                return false;
            case WFMappingPngCheck.ImageTooLarge:
                Popup("wf-mapping-template-image-too-large", ("side", WFMappingPngHeader.MaxSide));
                return false;
            default:
                Popup("wf-mapping-template-not-png");
                return false;
        }
    }

    /// <summary>Decodes PNG bytes; null when unreadable, with a popup when <paramref name="report"/> is set.</summary>
    private Texture? Decode(byte[] data, bool report)
    {
        try
        {
            return Texture.LoadFromPNGStream(new MemoryStream(data, false), "wf-mapping-template");
        }
        catch (Exception e)
        {
            Log.Debug($"Unreadable mapping template: {e.Message}");
            if (report)
                Popup("wf-mapping-template-not-png");

            return null;
        }
    }

    private void SetTexture(Texture? texture)
    {
        if (ReferenceEquals(Texture, texture))
            return;

        if (Texture is OwnedTexture owned)
            owned.Dispose();

        Texture = texture;
        if (texture != null && !_overlays.HasOverlay<WFMappingTemplateOverlay>())
            _overlays.AddOverlay(new WFMappingTemplateOverlay(this, _modes));
        else if (texture == null)
            _overlays.RemoveOverlay<WFMappingTemplateOverlay>();
    }

    private void WriteImage(ResPath folder, byte[] data)
    {
        try
        {
            if (!_resources.UserData.Exists(folder))
                _resources.UserData.CreateDir(folder);

            _resources.UserData.WriteAllBytes(folder / ImageFile, data);
        }
        catch (Exception e)
        {
            Log.Warning($"Writing the mapping template in {folder} failed: {e.Message}");
        }
    }

    private void SaveSettings()
    {
        _dirty = false;
        _nextSave = _timing.RealTime + SaveInterval;
        if (_folder is not { } folder || !HasTemplate)
            return;

        try
        {
            if (!_resources.UserData.Exists(folder))
                _resources.UserData.CreateDir(folder);

            _resources.UserData.WriteAllText(folder / SettingsFile, Settings.Write());
        }
        catch (Exception e)
        {
            Log.Warning($"Writing the mapping template settings in {folder} failed: {e.Message}");
        }
    }

    /// <summary>Reads a stream whole; null when it is longer than <paramref name="cap"/> bytes.</summary>
    private static byte[]? ReadCapped(Stream stream, int cap)
    {
        if (stream.CanSeek && stream.Length > cap)
            return null;

        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > cap)
                return null;

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private PointerInputCmdHandler Nudge(int x, int y)
    {
        return new PointerInputCmdHandler((ICommonSession? _, EntityCoordinates _, EntityUid _) => _mode.Nudge(x, y),
            outsidePrediction: true);
    }

    private void OnScreenLoad()
    {
        if (_ui.ActiveScreen is not WFMappingScreen screen)
            return;

        OnScreenUnload();
        _panel = new WFMappingTemplatePanel(this);
        _slot = _ui.GetUIController<WFMappingScreenUIController>()
            .AddDocked(screen.TemplateHost, _panel, "wf-mapping-template-title");
    }

    private void OnScreenUnload()
    {
        _panel?.Unbind();
        _panel = null;
        if (_slot is { } slot)
        {
            slot.Dock();
            slot.Orphan();
        }

        _slot = null;
    }
}
