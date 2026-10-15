using Content.Server.Chat.Managers;
using Content.Server._WF.ShipPa;
using Content.Shared.Roles;
using Robust.Shared.Enums;
using Robust.Shared.Network;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    [Dependency] private IChatManager _crewChat = default!;
    [Dependency] private ShipPaSystem _crewPa = default!;

    /// <summary>Confirms accepted membership directly, and announces it aboard an already launched crew ship.</summary>
    private void NotifyCrewJoined(Lobby lobby, NetUserId joined)
    {
        var member = lobby.Members[joined];
        var role = member.RoleId is { } id && lobby.Roles.TryGetValue(id, out var selected) ? selected.Name : string.Empty;
        if (string.IsNullOrEmpty(role))
            role = member.Job != null && _prototypes.TryIndex<JobPrototype>(member.Job, out var job)
                ? job.LocalizedName : Loc.GetString("wf-round-setup-job-unselected");
        var message = Loc.GetString("wf-round-setup-member-joined", ("name", member.Name), ("role", role));
        foreach (var user in lobby.Members.Keys)
        {
            if (_players.TryGetSessionById(user, out var session) && session.Status == SessionStatus.InGame)
                _crewChat.DispatchServerMessage(session, message);
        }
        if (!lobby.Started)
            return;
        var ships = EntityQueryEnumerator<WFShipScenarioComponent>();
        while (ships.MoveNext(out var grid, out var ship))
        {
            if (ship.LobbyId == lobby.Id)
                _crewPa.Announce(grid, message);
        }
        var wrecks = EntityQueryEnumerator<WFCrashScenarioComponent>();
        while (wrecks.MoveNext(out var grid, out var crash))
        {
            if (crash.LobbyId == lobby.Id)
                _crewPa.Announce(grid, message);
        }
    }
}
