using System.Linq;
using Content.Server._WG.AHelpTickets;
using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Shared._WG.LogInvestigator;
using Content.Shared.Administration;
using Content.Shared.Mind;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Network;

namespace Content.Server._WG.LogInvestigator;

/// <summary>
/// wglogs [round N] [player GUID|username]... [entity ID]... [text WORDS...]: opens the log investigator filled in.
/// </summary>
[AdminCommand(AdminFlags.Logs)]
public sealed class WGLogsCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private GameTicker _ticker = default!;

    public override string Command => "wglogs";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } admin)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        var query = new LogQuery { RoundId = _ticker.RoundId };
        var players = new List<Guid>();
        var entities = new List<int>();
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            var value = args[i + 1];
            switch (args[i])
            {
                case "round" when int.TryParse(value, out var round):
                    query.RoundId = round;
                    break;
                case "player" when Guid.TryParse(value, out var guid):
                    players.Add(guid);
                    break;
                case "player" when _players.TryGetUserId(value, out var user):
                    players.Add(user.UserId);
                    break;
                case "entity" when int.TryParse(value.TrimStart('n'), out var entity):
                    entities.Add(entity);
                    break;
                case "text":
                    query.Text = string.Join(' ', args.Skip(i + 1));
                    i = args.Length;
                    break;
                default:
                    shell.WriteError(Loc.GetString("cmd-wglogs-bad-arg", ("arg", args[i]), ("value", value)));
                    return;
            }
        }

        query.Players = players.ToArray();
        query.Entities = entities.ToArray();
        if (!LogInvestigatorEui.TryOpen(admin, query))
            shell.WriteError(Loc.GetString("cmd-wglogs-off"));
    }
}

/// <summary>wginvestigate KEY: opens the investigator on an ahelp ticket, with its encounter report.</summary>
[AdminCommand(AdminFlags.Logs)]
public sealed class WGInvestigateCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private AHelpTicketSystem _tickets = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMindSystem _minds = default!;

    /// <summary>How long before a ticket opened the investigation looks.</summary>
    private static readonly TimeSpan LookBack = TimeSpan.FromMinutes(15);

    public override string Command => "wginvestigate";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } admin)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length != 1 || !int.TryParse(args[0], out var key)
            || !_tickets.TryGetTicket(key, out var ticket, out var text, out var closed))
        {
            shell.WriteError(Loc.GetString("cmd-wginvestigate-no-ticket"));
            return;
        }

        var players = new List<Guid> { ticket.Player.UserId };
        players.AddRange(NamedPlayers(text).Where(p => p != ticket.Player.UserId));

        // The window runs in round time, from a while before the ticket opened until it closed.
        var start = DateTime.UtcNow - _ticker.RoundDuration();
        var query = new LogQuery
        {
            RoundId = ticket.RoundId,
            Players = players.Distinct().ToArray(),
            FromSeconds = Math.Max(0, (ticket.Opened - LookBack - start).TotalSeconds),
            ToSeconds = ((closed ?? DateTime.UtcNow) - start).TotalSeconds,
        };

        if (!LogInvestigatorEui.TryOpen(admin, query, LogReportKind.Encounter))
            shell.WriteError(Loc.GetString("cmd-wglogs-off"));
    }

    /// <summary>Players in the round whose character or account name the text mentions.</summary>
    private IEnumerable<Guid> NamedPlayers(IReadOnlyList<string> text)
    {
        var said = string.Join('\n', text);
        foreach (var session in _players.Sessions)
        {
            if (Mentions(said, session.Name)
                || _minds.GetCharacterName(session.UserId) is { } character && (Mentions(said, character) || character.Split(' ').Any(part => part.Length >= 4 && Mentions(said, part))))
                yield return session.UserId.UserId;
        }
    }

    /// <summary>Whether the text has the name as a whole word, ignoring case.</summary>
    public static bool Mentions(string text, string name)
    {
        if (name.Length < 3)
            return false;

        var index = 0;
        while ((index = text.IndexOf(name, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + name.Length;
            var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (before && after)
                return true;

            index = end;
        }

        return false;
    }
}
