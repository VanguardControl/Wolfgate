using Content.Shared._WG.Mentors;
using Robust.Shared.Network;

namespace Content.Client._WG.Mentors;

/// <summary>This player's mentor status from the server, and how many mentors are on duty.</summary>
public sealed class WGClientMentorSystem : EntitySystem
{
    public bool IsMentor { get; private set; }

    public MentorTier Tier { get; private set; }

    public bool OnDuty { get; private set; }

    public int OnDutyCount { get; private set; }

    /// <summary>For staff: the connected active mentors.</summary>
    public IReadOnlySet<NetUserId> Mentors => _mentors;

    /// <summary>Raised when the staff list of mentors changes.</summary>
    public event Action? RosterChanged;

    private readonly HashSet<NetUserId> _mentors = new();

    /// <summary>Raised when this player's status or duty changes.</summary>
    public event Action? StatusChanged;

    /// <summary>Raised when the on-duty count changes.</summary>
    public event Action? CountChanged;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMentorStatusEvent>(OnStatus);
        SubscribeNetworkEvent<WGMentorCountEvent>(OnCount);
        SubscribeNetworkEvent<WGMentorRosterEvent>(OnRoster);
    }

    private void OnRoster(WGMentorRosterEvent ev)
    {
        _mentors.Clear();
        _mentors.UnionWith(ev.Mentors);
        RosterChanged?.Invoke();
    }

    /// <summary>Asks the server to put this mentor on or off duty.</summary>
    public void RequestDuty(bool onDuty)
    {
        RaiseNetworkEvent(new WGMentorSetDutyEvent { OnDuty = onDuty });
    }

    private void OnStatus(WGMentorStatusEvent ev)
    {
        IsMentor = ev.IsMentor;
        Tier = ev.Tier;
        OnDuty = ev.OnDuty;
        StatusChanged?.Invoke();
    }

    private void OnCount(WGMentorCountEvent ev)
    {
        OnDutyCount = ev.OnDuty;
        CountChanged?.Invoke();
    }
}
