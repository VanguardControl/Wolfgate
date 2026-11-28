using System.IO;
using System.Numerics;
using System.Text;
using Content.Client._WG.EventStaff;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.Shared._WG.AHelpTickets;
using Content.Shared._WG.EventStaff;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Client._WG.AHelpTickets;

/// <summary>
/// The player's ahelp window when tickets are on. With no ticket open it is a form: what kind of issue, what
/// happened, Submit. Once a ticket is filed, or staff write first, it turns into the conversation as message bubbles,
/// with the ticket's number and state on top and an export to a text file.
/// </summary>
public sealed class WGPlayerAHelpUIHandler : IAHelpUIHandler
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IFileDialogManager _fileDialog = default!;

    private readonly NetUserId _owner;

    private DefaultWindow? _window;
    private BoxContainer? _form;
    private BoxContainer? _chatView;
    private WGAHelpChatPanel? _chat;
    private AHelpCategorySelector? _category;
    private TextEdit? _firstMessage;
    private Label? _closedNotice;
    private Button? _newTicket;
    private RichTextLabel? _relayLabel;
    private AHelpTicketClientSystem? _tickets;
    private bool _relayActive;

    /// <summary>Whether a line has been said in the conversation shown, so it opens on the chat rather than the form.</summary>
    private bool _talking;

    public WGPlayerAHelpUIHandler(NetUserId owner)
    {
        IoCManager.InjectDependencies(this);
        _owner = owner;
    }

    public bool IsAdmin => false;
    public bool IsOpen => _window is { Disposed: false, IsOpen: true };

    /// <summary>The kind of issue the player picked on the form.</summary>
    public AHelpCategory Category => _category?.Category ?? AHelpCategory.Player;

    public event Action? OnClose;
    public event Action? OnOpen;
    public Action<NetUserId, string, bool, bool>? SendMessageAction { get; set; }
    public event Action<NetUserId, string>? InputTextChanged;

    public void Receive(SharedBwoinkSystem.BwoinkTextMessage message)
    {
        EnsureInit();
        _chat!.ReceiveLine(message);
        if (!message.WGSystem)
            _talking = true;

        ShowView();
        _window!.OpenCentered();
    }

    public void Close()
    {
        _window?.Close();
    }

    public void ToggleWindow()
    {
        EnsureInit();
        if (_window!.IsOpen)
            _window.Close();
        else
            _window.OpenCentered();
    }

    public void Open(NetUserId netUserId, bool relayActive)
    {
        _relayActive = relayActive;
        EnsureInit();
        _window!.OpenCentered();
    }

    public void DiscordRelayChanged(bool active)
    {
        _relayActive = active;
        if (_relayLabel != null)
            _relayLabel.Visible = active;
    }

    public void PeopleTypingUpdated(BwoinkPlayerTypingUpdated args)
    {
        _chat?.UpdatePlayerTyping(args.PlayerName, args.Typing);
    }

    private void EnsureInit()
    {
        if (_window is { Disposed: false })
            return;

        _tickets = _entities.System<AHelpTicketClientSystem>();
        _tickets.OwnStatusChanged += OnStatus;

        _window = new DefaultWindow
        {
            TitleClass = "windowTitleAlert",
            HeaderClass = "windowHeaderAlert",
            Title = Loc.GetString("bwoink-user-title"),
            MinSize = new Vector2(480, 380),
            SetSize = new Vector2(560, 520),
        };
        _window.OnClose += () => OnClose?.Invoke();
        _window.OnOpen += () => OnOpen?.Invoke();

        var layout = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        layout.AddChild(new AHelpTicketStatusLabel());

        _relayLabel = new RichTextLabel { Visible = _relayActive };
        var relay = new FormattedMessage();
        relay.PushColor(Color.LightGray);
        relay.AddText(Loc.GetString("bwoink-system-messages-being-relayed-to-discord"));
        relay.Pop();
        _relayLabel.SetMessage(relay);
        layout.AddChild(_relayLabel);

        layout.AddChild(_form = BuildForm());
        layout.AddChild(_chatView = BuildChat());
        _window.Contents.AddChild(layout);

        ShowView();
    }

    private BoxContainer BuildForm()
    {
        var form = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true, SeparationOverride = 6 };
        var intro = new RichTextLabel();
        intro.SetMessage(FormattedMessage.FromMarkupPermissive(Loc.GetString("bwoink-system-introductory-message")));
        form.AddChild(intro);

        _category = new AHelpCategorySelector();
        form.AddChild(_category);

        form.AddChild(new Label { Text = Loc.GetString("wg-ahelp-player-form-message"), StyleClasses = { "LabelSubText" } });
        _firstMessage = new TextEdit { VerticalExpand = true, MinHeight = 120 };
        form.AddChild(_firstMessage);

        var submit = new Button { Text = Loc.GetString("wg-ahelp-player-form-submit"), StyleClasses = { "Caution" } };
        submit.OnPressed += _ => Submit();
        form.AddChild(submit);
        return form;
    }

    private BoxContainer BuildChat()
    {
        var view = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true, SeparationOverride = 4 };
        _chat = new WGAHelpChatPanel(_owner, _owner, true, text => SendMessageAction?.Invoke(_owner, text, true, false));
        _chat.InputTextChanged += text => InputTextChanged?.Invoke(_owner, text);
        view.AddChild(_chat);

        _closedNotice = new Label { Text = Loc.GetString("wg-ahelp-player-closed"), StyleClasses = { "LabelSubText" }, Visible = false };
        view.AddChild(_closedNotice);

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        var newTicket = _newTicket = new Button { Text = Loc.GetString("wg-ahelp-player-new-ticket"), StyleClasses = { "OpenRight" } };
        newTicket.OnPressed += _ =>
        {
            _talking = false;
            ShowView(form: true);
        };
        var export = new Button { Text = Loc.GetString("wg-ahelp-tickets-export"), StyleClasses = { "OpenLeft" } };
        export.OnPressed += _ => Export();
        buttons.AddChild(newTicket);
        buttons.AddChild(new Control { HorizontalExpand = true });
        buttons.AddChild(export);
        view.AddChild(buttons);
        return view;
    }

    /// <summary>Files the ticket with what the form says, then shows the conversation.</summary>
    private void Submit()
    {
        if (_firstMessage == null)
            return;

        var text = Rope.Collapse(_firstMessage.TextRope).Trim();
        if (text.Length == 0)
            return;

        SendMessageAction?.Invoke(_owner, text, true, false);
        _firstMessage.TextRope = new Rope.Leaf(string.Empty);
        _talking = true;
        ShowView();
        _chat?.Input.GrabKeyboardFocus();
    }

    private void OnStatus(AHelpTicketPlayerStatusMessage status)
    {
        ShowView();
    }

    /// <summary>The form when nothing is going on (or asked for), otherwise the conversation.</summary>
    private void ShowView(bool form = false)
    {
        if (_form == null || _chatView == null)
            return;

        var status = _tickets?.OwnStatus;
        var open = status != null && !status.State.IsClosed();
        var showForm = form || !open && !_talking;
        _form.Visible = showForm;
        _chatView.Visible = !showForm;
        if (_closedNotice != null)
            _closedNotice.Visible = status != null && status.State.IsClosed();

        // While a ticket is open, anything filed would only add to it.
        if (_newTicket != null)
        {
            _newTicket.Disabled = open;
            _newTicket.ToolTip = open ? Loc.GetString("wg-ahelp-player-new-ticket-open") : null;
        }
    }

    private async void Export()
    {
        if (_chat == null)
            return;

        var text = new StringBuilder();
        if (_tickets?.OwnStatus is { Number: > 0 } status)
            text.AppendLine(Loc.GetString("wg-ahelp-player-export-header", ("number", status.Number)));
        text.AppendLine();
        text.Append(_chat.Transcript());

        var file = await _fileDialog.SaveFile(new FileDialogFilters(new FileDialogFilters.Group("txt")));
        if (file == null)
            return;

        try
        {
            await using var writer = new StreamWriter(file.Value.fileStream, Encoding.UTF8);
            await writer.WriteAsync(text.ToString());
        }
        catch (Exception e)
        {
            Logger.GetSawmill("wg.ahelp").Error($"Saving the ahelp transcript failed: {e}");
        }
    }

    public void Dispose()
    {
        if (_tickets != null)
            _tickets.OwnStatusChanged -= OnStatus;

        _window?.Dispose();
        _window = null;
        _form = null;
        _chatView = null;
        _chat = null;
        _category = null;
        _firstMessage = null;
        _closedNotice = null;
        _newTicket = null;
        _relayLabel = null;
    }
}
