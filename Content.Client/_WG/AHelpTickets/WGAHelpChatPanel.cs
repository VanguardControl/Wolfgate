using System.Linq;
using System.Numerics;
using System.Text;
using Content.Shared.Administration;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WG.AHelpTickets;

/// <summary>
/// One ahelp conversation as message bubbles: the viewer's side on the right, the other side on the left, notices in
/// the middle. Staff see the player on the left and staff on the right, their own lines in a brighter blue; a player
/// sees themself on the right. Keeps the lines for export to a text file.
/// </summary>
public sealed class WGAHelpChatPanel : BoxContainer
{
    private static readonly Color OwnColor = Color.FromHex("#2f5f9e");
    private static readonly Color SameSideColor = Color.FromHex("#3b4a66");
    private static readonly Color OtherColor = Color.FromHex("#3a3a42");
    private static readonly Color AdminOnlyColor = Color.FromHex("#6b4a1f");
    private static readonly Color NoticeColor = Color.FromHex("#00000000");
    private static readonly TimeSpan TypingShown = TimeSpan.FromSeconds(10);

    private readonly NetUserId _channel;
    private readonly NetUserId? _viewer;
    private readonly bool _viewerIsPlayer;
    private readonly Action<string> _send;
    private readonly ScrollContainer _scroll;
    private readonly BoxContainer _lines;
    private readonly Label _typing;
    private readonly List<string> _transcript = new();
    private readonly Dictionary<string, TimeSpan> _typingLeft = new();

    /// <summary>Frames left in which to keep the view at the bottom, while layout catches up with a new line.</summary>
    private int _stickFrames;

    /// <summary>Lines that came in while this panel wasn't shown; system notices don't count.</summary>
    public int Unread { get; private set; }

    /// <summary>When the last player or staff line came in.</summary>
    public DateTime LastMessage { get; private set; } = DateTime.MinValue;

    /// <summary>The box the viewer types in.</summary>
    public LineEdit Input { get; }

    public event Action<string>? InputTextChanged;

    /// <param name="channel">The player whose ahelp this is.</param>
    /// <param name="viewer">Who is reading it.</param>
    /// <param name="viewerIsPlayer">Whether the reader is that player rather than staff.</param>
    /// <param name="send">Sends a typed line.</param>
    public WGAHelpChatPanel(NetUserId channel, NetUserId? viewer, bool viewerIsPlayer, Action<string> send)
    {
        _channel = channel;
        _viewer = viewer;
        _viewerIsPlayer = viewerIsPlayer;
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        VerticalExpand = true;
        HorizontalExpand = true;

        _lines = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6,
            Margin = new Thickness(4),
        };
        _scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, ReserveScrollbarSpace = true };
        _scroll.AddChild(_lines);
        AddChild(new PanelContainer
        {
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#1b1b22") },
            Children = { _scroll },
        });

        _typing = new Label { StyleClasses = { "LabelSubText" }, Text = " " };
        AddChild(_typing);

        var inputRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        Input = new LineEdit { HorizontalExpand = true, PlaceHolder = Loc.GetString("wg-ahelp-chat-placeholder") };
        Input.OnTextEntered += args => Send(args.Text);
        Input.OnTextChanged += args => InputTextChanged?.Invoke(args.Text);
        var sendButton = new Button { Text = Loc.GetString("wg-ahelp-chat-send"), StyleClasses = { "OpenLeft" } };
        sendButton.OnPressed += _ => Send(Input.Text);
        inputRow.AddChild(Input);
        inputRow.AddChild(sendButton);
        AddChild(inputRow);

        OnVisibilityChanged += c =>
        {
            if (!c.Visible)
                return;

            Unread = 0;
            _stickFrames = 2;
        };
    }

    private void Send(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _send(text);
        Input.Clear();
        _stickFrames = 2;
    }

    /// <summary>Adds a line as a bubble on the side it belongs to.</summary>
    public void ReceiveLine(SharedBwoinkSystem.BwoinkTextMessage message)
    {
        var notice = message.WGSystem;
        if (!Visible && !notice)
            Unread++;
        if (!notice)
            LastMessage = message.SentAt;

        var fromPlayer = message.TrueSender == _channel;
        var own = _viewer is { } viewer && message.TrueSender == viewer;
        var side = notice
            ? WGChatBubbleSide.Center
            : fromPlayer == _viewerIsPlayer ? WGChatBubbleSide.Right : WGChatBubbleSide.Left;
        var color = notice
            ? NoticeColor
            : message.AdminOnly
                ? AdminOnlyColor
                : own ? OwnColor : side == WGChatBubbleSide.Right ? SameSideColor : OtherColor;

        var text = new FormattedMessage();
        if (!text.TryAddMarkup(message.Text, out _))
            text.AddText(message.Text);

        var body = new BoxContainer { Orientation = LayoutOrientation.Vertical };
        var label = new RichTextLabel();
        label.SetMessage(text);
        body.AddChild(label);
        body.AddChild(new Label
        {
            Text = message.SentAt.ToShortTimeString(),
            StyleClasses = { "LabelSubText" },
            HorizontalAlignment = side == WGChatBubbleSide.Left ? HAlignment.Left : HAlignment.Right,
        });

        var bubble = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = color,
                BorderColor = notice ? Color.FromHex("#55555f") : color,
                BorderThickness = notice ? new Thickness(0, 0, 0, 1) : new Thickness(0),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 2,
            },
            Children = { body },
        };

        var atBottom = _scroll.GetScrollValue().Y >= _lines.Size.Y - _scroll.Size.Y - 8;
        _lines.AddChild(new WGChatBubbleRow(side, bubble));
        if (atBottom || own || fromPlayer == _viewerIsPlayer)
            _stickFrames = 2;

        var plain = FormattedMessage.RemoveMarkupPermissive(message.Text);
        _transcript.Add($"[{message.SentAt:yyyy-MM-dd HH:mm:ss}] {plain}");
    }

    /// <summary>Every line so far as plain text, oldest first.</summary>
    public string Transcript()
    {
        var text = new StringBuilder();
        foreach (var line in _transcript)
        {
            text.AppendLine(line);
        }

        return text.ToString();
    }

    /// <summary>Shows who else is typing; each name fades after a while without an update.</summary>
    public void UpdatePlayerTyping(string name, bool typing)
    {
        if (typing)
            _typingLeft[name] = TypingShown;
        else
            _typingLeft.Remove(name);

        UpdateTyping();
    }

    private void UpdateTyping()
    {
        _typing.Text = _typingLeft.Count == 0
            ? " "
            : Loc.GetString("bwoink-system-typing-indicator",
                ("players", string.Join(", ", _typingLeft.Keys)),
                ("count", _typingLeft.Count));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_typingLeft.Count > 0)
        {
            var delta = TimeSpan.FromSeconds(args.DeltaSeconds);
            foreach (var name in _typingLeft.Keys.ToList())
            {
                _typingLeft[name] -= delta;
                if (_typingLeft[name] <= TimeSpan.Zero)
                    _typingLeft.Remove(name);
            }

            if (_typingLeft.Count == 0)
                UpdateTyping();
        }

        if (_stickFrames <= 0)
            return;

        _stickFrames--;
        _scroll.SetScrollValue(new Vector2(0, float.MaxValue));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            InputTextChanged = null;
    }
}
