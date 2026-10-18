using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.MappingMode;

/// <summary>The running download's progress with Cancel, or the finished one's Open folder.</summary>
public sealed class WFMappingDownloadRow : BoxContainer
{
    private readonly Label _label;
    private readonly Button _cancel;
    private readonly Button _openFolder;

    public event Action? CancelPressed;
    public event Action? OpenFolderPressed;

    public WFMappingDownloadRow()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
        Visible = false;

        _label = new Label { HorizontalExpand = true, ClipText = true };
        _cancel = new Button { Text = Loc.GetString("wf-mapping-mode-download-cancel") };
        _openFolder = new Button { Text = Loc.GetString("wf-mapping-mode-download-open-folder") };
        _cancel.OnPressed += _ => CancelPressed?.Invoke();
        _openFolder.OnPressed += _ => OpenFolderPressed?.Invoke();

        AddChild(_label);
        AddChild(_cancel);
        AddChild(_openFolder);
    }

    public void SetProgress(WFMappingDownloadProgress? progress)
    {
        Visible = progress != null;
        if (progress == null)
            return;

        _cancel.Visible = progress.Active;
        _openFolder.Visible = !progress.Active;
        _label.Text = progress.Active
            ? Loc.GetString("wf-mapping-mode-download-progress",
                ("name", progress.MapName),
                ("version", progress.Version),
                ("file", progress.FileIndex + 1),
                ("files", progress.FileCount),
                ("percent", progress.ChunkCount == 0 ? 0 : progress.Chunks * 100 / progress.ChunkCount))
            : Loc.GetString("wf-mapping-mode-download-ready", ("name", progress.MapName), ("version", progress.Version));
    }
}
