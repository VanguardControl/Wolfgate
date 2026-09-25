using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Cracker.BUI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.PlanetCracker.Cracker;

/// <summary>The crack stages as a strip of cells, with the active countdown and the cut's progress bar.</summary>
public sealed class WFCrackTimeline : WFDiagramControl
{
    /// <summary>The nine stages, in order; one cell each.</summary>
    private static readonly WFCrackState[] Stages =
    {
        WFCrackState.Idle,
        WFCrackState.Surveying,
        WFCrackState.AnchorsPlaced,
        WFCrackState.AnchorsLocked,
        WFCrackState.Cracking,
        WFCrackState.Cracked,
        WFCrackState.Disconnecting,
        WFCrackState.Released,
        WFCrackState.Falling,
    };

    /// <summary>Gap between stage cells, in virtual pixels.</summary>
    private const float CellGap = 2f;

    /// <summary>Height of the crack progress bar, in virtual pixels.</summary>
    private const float BarHeight = 10f;

    private readonly StyleBoxFlat[] _cellBoxes = new StyleBoxFlat[Stages.Length];

    private readonly Label[] _cellLabels = new Label[Stages.Length];

    private readonly Label _countdown;

    private readonly ProgressBar _progress;

    private readonly StyleBoxFlat _progressTrack = new();

    private readonly StyleBoxFlat _progressFill = new();

    public WFCrackTimeline()
    {
        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };

        var strip = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        for (var i = 0; i < Stages.Length; i++)
        {
            var name = Loc.GetString(StageKey(Stages[i]));

            var label = new Label
            {
                // Short names; nine full ones do not fit the strip.
                Text = Loc.GetString(StageKey(Stages[i]).Replace("-state-", "-stage-short-")),
                Align = Label.AlignMode.Center,
                ClipText = true,
                HorizontalExpand = true,
            };

            label.StyleClasses.Add("LabelSubText");

            var box = new StyleBoxFlat
            {
                ContentMarginLeftOverride = 3,
                ContentMarginRightOverride = 3,
                ContentMarginTopOverride = 2,
                ContentMarginBottomOverride = 2,
            };

            var cell = new PanelContainer
            {
                PanelOverride = box,
                HorizontalExpand = true,
                // Full name, since the cell may clip it.
                ToolTip = name,
                Margin = new Thickness(0f, 0f, i == Stages.Length - 1 ? 0f : CellGap, 0f),
            };

            cell.AddChild(label);
            strip.AddChild(cell);

            _cellBoxes[i] = box;
            _cellLabels[i] = label;
        }

        _countdown = new Label
        {
            ClipText = true,
            HorizontalExpand = true,
            Margin = new Thickness(0f, 3f, 0f, 2f),
        };

        _progress = new ProgressBar
        {
            MinValue = 0f,
            MaxValue = 1f,
            Value = 0f,
            MinHeight = BarHeight,
            HorizontalExpand = true,
            BackgroundStyleBoxOverride = _progressTrack,
            ForegroundStyleBoxOverride = _progressFill,
        };

        root.AddChild(strip);
        root.AddChild(_countdown);
        root.AddChild(_progress);
        AddChild(root);

        Apply(null);
    }

    /// <summary>The state the strip shows; null until the console has pushed one.</summary>
    public void SetState(WFCrackConsoleState? state)
    {
        Apply(state);
    }

    /// <summary>Writes one state into the cells, the countdown line and the fill bar.</summary>
    private void Apply(WFCrackConsoleState? state)
    {
        RefreshSkin();

        var current = state?.State ?? WFCrackState.Idle;
        var currentIndex = Array.IndexOf(Stages, current);

        for (var i = 0; i < Stages.Length; i++)
        {
            _cellBoxes[i].BackgroundColor = i == currentIndex
                ? Skin.Accent
                : i < currentIndex
                    ? Skin.AccentDim
                    : Skin.Glass;

            _cellLabels[i].FontColorOverride = i == currentIndex
                ? Skin.Ink
                : i < currentIndex
                    ? Skin.Text
                    : Skin.TextMuted;
        }

        _countdown.Text = CountdownText(state);
        _countdown.FontColorOverride = CountdownColour(state);

        _progress.Value = CrackFraction(state);
        _progressTrack.BackgroundColor = Skin.Glass;
        _progressFill.BackgroundColor = state is { CrackPaused: true } ? Skin.Caution : Skin.Good;
    }

    /// <summary>How far through the cut the hull is, 0 to 1.</summary>
    private static float CrackFraction(WFCrackConsoleState? state)
    {
        if (state is not { } pushed)
            return 0f;

        var total = (float)pushed.CrackTotal.TotalSeconds;

        if (total <= 0f)
            return 0f;

        return Math.Clamp(1f - (float)pushed.CrackRemaining.TotalSeconds / total, 0f, 1f);
    }

    /// <summary>The one countdown the stage carries, most urgent first; nominal when it carries none.</summary>
    private static string CountdownText(WFCrackConsoleState? state)
    {
        if (state is not { } pushed)
            return Loc.GetString("wf-crack-console-grace-nominal");

        if (pushed.EvacRunning)
            return Loc.GetString("wf-crack-console-evac", ("time", Format(pushed.EvacRemaining)));

        if (pushed.DisconnectArmed)
            return Loc.GetString("wf-crack-console-disconnect", ("time", Format(pushed.DisconnectRemaining)));

        if (pushed.GraceRunning)
            return Loc.GetString("wf-crack-console-grace", ("time", Format(pushed.GraceRemaining)));

        if (pushed.CrackPaused)
            return Loc.GetString("wf-crack-console-crack-paused");

        return pushed.State == WFCrackState.Cracking
            ? Loc.GetString("wf-crack-console-crack-remaining", ("time", Format(pushed.CrackRemaining)))
            : Loc.GetString("wf-crack-console-grace-nominal");
    }

    /// <summary>Danger for a running countdown, caution for a held cut, muted for nominal.</summary>
    private Color CountdownColour(WFCrackConsoleState? state)
    {
        if (state is not { } pushed)
            return Skin.TextMuted;

        if (pushed.EvacRunning || pushed.DisconnectArmed || pushed.GraceRunning)
            return Skin.Danger;

        if (pushed.CrackPaused)
            return Skin.Caution;

        return pushed.State == WFCrackState.Cracking ? Skin.Text : Skin.TextMuted;
    }

    /// <summary>Formats a timer as m:ss.</summary>
    private static string Format(TimeSpan time)
    {
        return time <= TimeSpan.Zero ? "0:00" : $"{(int)time.TotalMinutes}:{time.Seconds:D2}";
    }

    /// <summary>Locale key naming one stage.</summary>
    private static string StageKey(WFCrackState state)
    {
        return state switch
        {
            WFCrackState.Idle => "wf-crack-console-state-idle",
            WFCrackState.Surveying => "wf-crack-console-state-surveying",
            WFCrackState.AnchorsPlaced => "wf-crack-console-state-anchors-placed",
            WFCrackState.AnchorsLocked => "wf-crack-console-state-anchors-locked",
            WFCrackState.Cracking => "wf-crack-console-state-cracking",
            WFCrackState.Cracked => "wf-crack-console-state-cracked",
            WFCrackState.Disconnecting => "wf-crack-console-state-disconnecting",
            WFCrackState.Released => "wf-crack-console-state-released",
            WFCrackState.Falling => "wf-crack-console-state-falling",
            _ => "wf-crack-console-state-idle",
        };
    }
}
