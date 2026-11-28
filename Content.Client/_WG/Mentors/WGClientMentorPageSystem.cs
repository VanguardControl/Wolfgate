using Content.Shared._WG.Mentors;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;

namespace Content.Client._WG.Mentors;

/// <summary>
/// The client side of becoming a mentor, the staff Mentors tab and the leaderboard: requests to the server and its
/// answers as C# events for the windows.
/// </summary>
public sealed class WGClientMentorPageSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IUriOpener _uri = default!;

    public event Action<WGMentorPageStateEvent>? PageReceived;
    public event Action<WGMentorStaffRosterEvent>? RosterReceived;
    public event Action<WGMentorStaffApplicationsEvent>? ApplicationsReceived;
    public event Action<WGMentorStaffTicketsEvent>? TicketsReceived;
    public event Action<WGMentorTranscriptEvent>? TranscriptReceived;
    public event Action<string>? StaffResult;
    public event Action<WGMentorBoardEvent>? BoardReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMentorPageStateEvent>(ev => PageReceived?.Invoke(ev));
        SubscribeNetworkEvent<WGMentorStaffRosterEvent>(ev => RosterReceived?.Invoke(ev));
        SubscribeNetworkEvent<WGMentorStaffApplicationsEvent>(ev => ApplicationsReceived?.Invoke(ev));
        SubscribeNetworkEvent<WGMentorStaffTicketsEvent>(ev => TicketsReceived?.Invoke(ev));
        SubscribeNetworkEvent<WGMentorTranscriptEvent>(ev => TranscriptReceived?.Invoke(ev));
        SubscribeNetworkEvent<WGMentorStaffResultEvent>(ev => StaffResult?.Invoke(ev.Message));
        SubscribeNetworkEvent<WGMentorBoardEvent>(ev => BoardReceived?.Invoke(ev));
        SubscribeNetworkEvent<WGMentorDiscordLinkEvent>(OnDiscordLink);
        SubscribeNetworkEvent<WGMentorEligibleNoticeEvent>(_ => _cfg.SetCVar(MentorCVars.EligibleNoticed, true));
    }

    private void OnDiscordLink(WGMentorDiscordLinkEvent ev)
    {
        // No System.Uri in the sandbox: only open what looks like a web link.
        if (ev.Url.StartsWith("https://", StringComparison.Ordinal) || ev.Url.StartsWith("http://", StringComparison.Ordinal))
            _uri.OpenUri(ev.Url);
    }

    public void RequestPage()
    {
        RaiseNetworkEvent(new WGMentorPageRequestEvent());
    }

    public void Apply(Dictionary<string, string> answers, bool agreed)
    {
        RaiseNetworkEvent(new WGMentorApplyEvent { Answers = answers, AgreedToCode = agreed });
    }

    public void PageAction(MentorPageAction action)
    {
        RaiseNetworkEvent(new WGMentorPageActionEvent { Action = action });
    }

    public void RequestStaff(MentorStaffView view, string search = "")
    {
        RaiseNetworkEvent(new WGMentorStaffRequestEvent { View = view, Search = search });
    }

    public void RequestTranscript(int id)
    {
        RaiseNetworkEvent(new WGMentorTranscriptRequestEvent { Id = id });
    }

    public void StaffAction(MentorStaffAction action, string player, MentorTier tier, string text)
    {
        RaiseNetworkEvent(new WGMentorStaffActionEvent { Action = action, Player = player, Tier = tier, Text = text });
    }

    public void Review(int id, bool approve, string reason, int reapplyDays)
    {
        RaiseNetworkEvent(new WGMentorReviewEvent { Id = id, Approve = approve, Reason = reason, ReapplyDays = reapplyDays });
    }

    public void RequestBoard(MentorBoardPeriod period)
    {
        RaiseNetworkEvent(new WGMentorBoardRequestEvent { Period = period });
    }
}
