using System.Linq;
using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.PoiArrivals;

/// <summary>Lists the points of interest still to arrive this shift, or jumps one or all of them in now.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WGPoiArrivalCommand : LocalizedEntityCommands
{
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WGPoiArrivalSystem _arrivals = default!;

    private const string All = "all";

    public override string Command => "wg_poiarrivals";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteLine(Help);
            return;
        }

        var pending = _arrivals.Pending();
        if (args.Length == 0)
        {
            if (pending.Count == 0)
            {
                shell.WriteLine(Loc.GetString("cmd-wg_poiarrivals-none"));
                return;
            }

            var now = _ticker.RoundDuration();
            foreach (var (id, due) in pending)
            {
                var left = due > now ? due - now : TimeSpan.Zero;
                shell.WriteLine(Loc.GetString("cmd-wg_poiarrivals-entry", ("id", id),
                    ("due", due.ToString(@"hh\:mm\:ss")), ("left", left.ToString(@"hh\:mm\:ss"))));
            }

            return;
        }

        var ids = args[0].Equals(All, StringComparison.OrdinalIgnoreCase)
            ? pending.Select(entry => entry.Id).ToList()
            : pending.Where(entry => entry.Id.Equals(args[0], StringComparison.OrdinalIgnoreCase)).Select(entry => entry.Id).ToList();

        if (ids.Count == 0)
        {
            shell.WriteError(Loc.GetString("cmd-wg_poiarrivals-not-pending", ("id", args[0])));
            return;
        }

        foreach (var id in ids)
        {
            if (_arrivals.WarpIn(id))
                shell.WriteLine(Loc.GetString("cmd-wg_poiarrivals-forced", ("id", id)));
            else
                shell.WriteError(Loc.GetString("cmd-wg_poiarrivals-failed", ("id", id)));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var options = _arrivals.Pending().Select(entry => entry.Id).Append(All);
        return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-wg_poiarrivals-hint"));
    }
}
