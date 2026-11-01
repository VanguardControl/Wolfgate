using System.Numerics;
using Content.Client._WF.GameHub;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._WF.DoomArcade;

/// <summary>Downloads Freedoom if needed, then runs it. Opened by a cabinet or the PDA Gamehub.</summary>
public sealed class DoomArcadeWindow : DefaultWindow
{
    private const string Game = WFGameHubSystem.DoomGame;
    private const float BytesPerMegabyte = 1024f * 1024f;

    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private WFGameDataManager _data = default!;

    private readonly BoxContainer _downloadPanel;
    private readonly Label _message;
    private readonly ProgressBar _progress;
    private readonly Button _retry;
    private readonly Label _failed;
    private EntityUid? _source;
    private bool _downloadStarted;

    public readonly DoomArcadeScreen Screen = new();

    public DoomArcadeWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("doom-arcade-window-title");
        SetSize = new Vector2(800, 680);
        MinSize = new Vector2(320, 320);

        _failed = new Label { Text = Loc.GetString("doom-arcade-window-failed"), Visible = false };
        Screen.Visible = false;
        Screen.Failed += () =>
        {
            Screen.Visible = false;
            _failed.Visible = true;
        };
        Screen.Quit += () => Close();

        _message = new Label { Text = Loc.GetString("wf-gamehub-checking"), HorizontalAlignment = HAlignment.Center };
        _progress = new ProgressBar { MinValue = 0, MaxValue = 1, MinHeight = 24, Margin = new Thickness(40, 8) };
        _retry = new Button { Text = Loc.GetString("wf-gamehub-retry"), HorizontalAlignment = HAlignment.Center, Visible = false };
        _retry.OnPressed += _ => BeginDownload();

        _downloadPanel = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            VerticalAlignment = VAlignment.Center,
            Children = { _message, _progress, _retry },
        };

        var leaderboard = new Button { Text = Loc.GetString("wf-gamehub-leaderboard"), HorizontalAlignment = HAlignment.Right };
        leaderboard.OnPressed += _ => _systems.GetEntitySystem<WFGameHubSystem>().OpenLeaderboard(Game);

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children = { leaderboard, _downloadPanel, Screen, _failed },
        });
    }

    /// <summary>Starts up for the cabinet or PDA that nearby players will hear the game from.</summary>
    public void Begin(EntityUid source)
    {
        _source = source;
        _systems.GetEntitySystem<WFGameHubSystem>().RequestManifest(Game);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_source == null || Screen.Running || _failed.Visible)
            return;

        var status = _data.GetStatus(Game);
        var (received, total) = _data.GetProgress(Game);

        _retry.Visible = status == WFGameDataStatus.Failed;

        switch (status)
        {
            case WFGameDataStatus.Missing when !_downloadStarted:
                BeginDownload();
                break;
            case WFGameDataStatus.Downloading:
                _progress.Value = total > 0 ? (float) received / total : 0f;
                _message.Text = Loc.GetString("wf-gamehub-downloading",
                    ("received", (received / BytesPerMegabyte).ToString("0.0")),
                    ("total", (total / BytesPerMegabyte).ToString("0.0")));
                break;
            case WFGameDataStatus.Failed:
                _message.Text = Loc.GetString("wf-gamehub-download-failed");
                break;
            case WFGameDataStatus.Ready:
                StartGame();
                break;
        }
    }

    private void BeginDownload()
    {
        _downloadStarted = true;
        _systems.GetEntitySystem<WFGameHubSystem>().StartDownload(Game);
    }

    private void StartGame()
    {
        _downloadPanel.Visible = false;
        Screen.Visible = true;
        Screen.Start(_source!.Value);
    }
}
