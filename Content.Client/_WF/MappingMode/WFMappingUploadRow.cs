using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.MappingMode;

/// <summary>The running upload's progress with Cancel, or the finished one with Open.</summary>
public sealed class WFMappingUploadRow : BoxContainer
{
    private readonly Label _label;
    private readonly Button _cancel;
    private readonly Button _open;
    private WFMappingUploadProgress? _progress;
    private bool _showOpen = true;

    public event Action? CancelPressed;

    /// <summary>Raised with the uploaded map and its version folder.</summary>
    public event Action<string, string>? OpenPressed;

    /// <summary>Whether the finished upload can be opened from here; not inside a session.</summary>
    public bool ShowOpen
    {
        get => _showOpen;
        set
        {
            _showOpen = value;
            SetProgress(_progress);
        }
    }

    public WFMappingUploadRow()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
        Visible = false;

        _label = new Label { HorizontalExpand = true, ClipText = true };
        _cancel = new Button { Text = Loc.GetString("wf-mapping-mode-upload-cancel") };
        _open = new Button { Text = Loc.GetString("wf-mapping-mode-upload-open") };
        _cancel.OnPressed += _ => CancelPressed?.Invoke();
        _open.OnPressed += _ =>
        {
            if (_progress is { Stage: WFMappingUploadStage.Done } done)
                OpenPressed?.Invoke(done.MapName, done.Version);
        };

        AddChild(_label);
        AddChild(_cancel);
        AddChild(_open);
    }

    public void SetProgress(WFMappingUploadProgress? progress)
    {
        _progress = progress;
        Visible = progress != null;
        if (progress == null)
            return;

        var done = progress.Stage == WFMappingUploadStage.Done;
        _cancel.Visible = !done;
        _open.Visible = done && _showOpen;
        _label.Text = progress.Stage switch
        {
            WFMappingUploadStage.Starting => Loc.GetString("wf-mapping-mode-upload-starting", ("name", progress.MapName)),
            WFMappingUploadStage.Sending => Loc.GetString("wf-mapping-mode-upload-progress", ("name", progress.MapName),
                ("percent", progress.ChunkCount == 0 ? 0 : progress.Acked * 100 / progress.ChunkCount)),
            WFMappingUploadStage.Checking => Loc.GetString("wf-mapping-mode-upload-checking", ("name", progress.MapName)),
            _ => Loc.GetString("wf-mapping-mode-upload-ready", ("name", progress.MapName), ("version", progress.Version)),
        };
    }
}
