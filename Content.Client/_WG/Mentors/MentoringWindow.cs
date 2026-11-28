using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared._WG.Mentors;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WG.Mentors;

/// <summary>
/// The Mentoring page from the Esc menu: progress toward the hours, what stops an application, the form and the Code
/// of Conduct, and for mentors their status with Resign or Reactivate. Shows the Discord link state and Link Discord.
/// </summary>
public sealed class MentoringWindow : DefaultWindow
{
    public static readonly ProtoId<GuideEntryPrototype> CodeOfConduct = "WGMentorCodeOfConduct";

    public event Action<Dictionary<string, string>, bool>? ApplyPressed;
    public event Action<MentorPageAction>? ActionPressed;

    private readonly RichTextLabel _status = Wrapped();
    private readonly BoxContainer _blocks = Column();
    private readonly BoxContainer _form = Column();
    private readonly Dictionary<string, LineEdit> _answers = new();
    private readonly CheckBox _agree = new();
    private readonly Button _apply = new();
    private readonly ConfirmButton _resign = new();
    private readonly Button _reactivate = new();
    private readonly RichTextLabel _discord = Wrapped();
    private readonly Button _link = new();

    public MentoringWindow()
    {
        Title = Loc.GetString("wg-mentor-page-title");
        MinSize = new Vector2(460, 420);
        SetSize = new Vector2(520, 560);

        var root = Column();
        root.Margin = new Thickness(8);
        root.AddChild(Wrapped(Loc.GetString("wg-mentor-page-intro")));
        root.AddChild(_status);
        root.AddChild(_blocks);

        var code = new Button { Text = Loc.GetString("wg-mentor-page-read-code"), HorizontalAlignment = HAlignment.Left };
        code.OnPressed += _ => IoCManager.Resolve<IUserInterfaceManager>()
            .GetUIController<GuidebookUIController>()
            .OpenGuidebook(selected: CodeOfConduct);
        root.AddChild(code);

        _form.AddChild(Wrapped(Loc.GetString("form-intro")));
        foreach (var id in MentorForm.Questions)
        {
            _form.AddChild(Wrapped(Loc.GetString($"form-{id}")));
            var answer = new LineEdit { HorizontalExpand = true, IsValid = t => t.Length <= MentorForm.MaxAnswerLength };
            _answers[id] = answer;
            _form.AddChild(answer);
        }

        _agree.Text = Loc.GetString("wg-mentor-page-agree");
        _agree.OnToggled += _ => _apply.Disabled = !_agree.Pressed;
        _form.AddChild(_agree);

        _apply.Text = Loc.GetString("wg-mentor-page-apply");
        _apply.Disabled = true;
        _apply.HorizontalAlignment = HAlignment.Left;
        _apply.OnPressed += _ => ApplyPressed?.Invoke(_answers.ToDictionary(a => a.Key, a => a.Value.Text), _agree.Pressed);
        _form.AddChild(_apply);

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_form);
        root.AddChild(scroll);

        _resign.Text = Loc.GetString("wg-mentor-page-resign");
        _resign.HorizontalAlignment = HAlignment.Left;
        _resign.OnPressed += _ => ActionPressed?.Invoke(MentorPageAction.Resign);
        root.AddChild(_resign);

        _reactivate.Text = Loc.GetString("wg-mentor-page-reactivate");
        _reactivate.HorizontalAlignment = HAlignment.Left;
        _reactivate.OnPressed += _ => ActionPressed?.Invoke(MentorPageAction.Reactivate);
        root.AddChild(_reactivate);

        var discordRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        _discord.HorizontalExpand = true;
        discordRow.AddChild(_discord);
        _link.Text = Loc.GetString("wg-mentor-page-link");
        _link.OnPressed += _ => ActionPressed?.Invoke(MentorPageAction.LinkDiscord);
        discordRow.AddChild(_link);
        root.AddChild(discordRow);

        Contents.AddChild(root);
        SetState(new WGMentorPageStateEvent { Status = MentorPageStatus.Disabled });
    }

    public void SetState(WGMentorPageStateEvent state)
    {
        var apply = state.Status == MentorPageStatus.Apply;
        Set(_status, state.Status switch
        {
            MentorPageStatus.Apply => Loc.GetString("wg-mentor-page-progress",
                ("hours", (int) state.Hours), ("needed", (int) state.NeededHours)),
            MentorPageStatus.Pending => Loc.GetString("wg-mentor-page-pending"),
            MentorPageStatus.Active => Loc.GetString("wg-mentor-page-status", ("tier", TierName(state.Tier))),
            MentorPageStatus.Inactive => Loc.GetString("wg-mentor-page-inactive"),
            MentorPageStatus.Suspended => Loc.GetString("wg-mentor-suspended"),
            _ => "",
        });

        _blocks.RemoveAllChildren();
        foreach (var block in state.Blocks)
        {
            var line = Wrapped();
            line.SetMessage(FormattedMessage.FromUnformatted(BlockText(block, state)), defaultColor: Color.Khaki);
            _blocks.AddChild(line);
        }

        _form.Visible = apply && state.Blocks.Count == 0;
        _resign.ConfirmationText = Loc.GetString("wg-mentor-page-resign-confirm", ("days", state.ReapplyDays));
        _resign.Visible = state.Status is MentorPageStatus.Active or MentorPageStatus.Inactive or MentorPageStatus.Suspended;
        _reactivate.Visible = state.Status == MentorPageStatus.Inactive;

        Set(_discord, state.Discord == MentorDiscordState.Linked
            ? Loc.GetString("wg-mentor-page-discord-linked")
            : Loc.GetString("wg-mentor-page-discord-unlinked"));
        _discord.Parent!.Visible = state.Discord != MentorDiscordState.Unavailable && state.Status != MentorPageStatus.Disabled;
        _link.Visible = state.Discord == MentorDiscordState.Unlinked;
    }

    private static string BlockText(MentorBlock block, WGMentorPageStateEvent state)
    {
        return block switch
        {
            MentorBlock.Hours => Loc.GetString("wg-mentor-page-no-hours", ("needed", (int) state.NeededHours)),
            MentorBlock.Ban => Loc.GetString("wg-mentor-page-no-ban"),
            MentorBlock.MentorBan => state.MentorBanUntil is { } until
                ? Loc.GetString("wg-mentor-page-no-mentor-ban", ("until", until.ToString("yyyy-MM-dd")))
                : Loc.GetString("wg-mentor-page-no-mentor-ban-permanent"),
            MentorBlock.Record => Loc.GetString("wg-mentor-page-no-record", ("days", state.CleanDays)),
            _ => Loc.GetString("wg-mentor-page-no-reapply",
                ("date", state.ReapplyAfter?.ToString("yyyy-MM-dd") ?? "")),
        };
    }

    public static string TierName(MentorTier tier)
    {
        return Loc.GetString(tier switch
        {
            MentorTier.Senior => "wg-mentor-tier-senior",
            MentorTier.Mentor => "wg-mentor-tier-mentor",
            _ => "wg-mentor-tier-trainee",
        });
    }

    private static BoxContainer Column()
    {
        return new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
    }

    private static RichTextLabel Wrapped(string text = "")
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        Set(label, text);
        return label;
    }

    private static void Set(RichTextLabel label, string text)
    {
        label.SetMessage(FormattedMessage.FromUnformatted(text));
    }
}
