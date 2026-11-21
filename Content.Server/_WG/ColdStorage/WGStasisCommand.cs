using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.ColdStorage;

/// <summary>Shows grid stasis, and wakes, pins or unpins a grid by hand.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class WGStasisCommand : LocalizedEntityCommands
{
    private const string AdminPin = "admin";
    private const int Rows = 20;

    [Dependency] private WGGridStasisSystem _stasis = default!;
    [Dependency] private WGEntityPressureSystem _pressure = default!;

    public override string Command => "wg_stasis";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            Summary(shell);
            return;
        }

        if (args.Length != 2 || args[0] is not ("wake" or "pin" or "unpin"))
        {
            shell.WriteError(Loc.GetString("cmd-wg_stasis-usage"));
            return;
        }

        if (!NetEntity.TryParse(args[1], out var net) || !EntityManager.TryGetEntity(net, out var grid)
            || !EntityManager.HasComponent<MapGridComponent>(grid))
        {
            shell.WriteError(Loc.GetString("cmd-wg_stasis-no-grid", ("grid", args[1])));
            return;
        }

        switch (args[0])
        {
            case "wake":
                _stasis.Wake(grid.Value);
                shell.WriteLine(Loc.GetString("cmd-wg_stasis-woken", ("grid", args[1])));
                break;
            case "pin":
                _stasis.Pin(grid.Value, AdminPin);
                shell.WriteLine(Loc.GetString("cmd-wg_stasis-pinned", ("grid", args[1])));
                break;
            default:
                _stasis.Unpin(grid.Value, AdminPin);
                shell.WriteLine(Loc.GetString("cmd-wg_stasis-unpinned", ("grid", args[1]),
                    ("held", _stasis.IsPinned(grid.Value))));
                break;
        }
    }

    private void Summary(IConsoleShell shell)
    {
        var counts = new int[Enum.GetValues<WGGridStasisState>().Length];
        var excluded = 0;
        var pinned = 0;
        var asleep = new List<(EntityUid Grid, int Paused)>();
        foreach (var (uid, rec) in _stasis.Grids)
        {
            if (rec.Policy == WGStasisPolicy.Never)
                excluded++;
            else
                counts[(int) rec.State]++;

            if (_stasis.IsPinned(uid))
                pinned++;

            if (_stasis.IsInStasis(uid))
                asleep.Add((uid, rec.Paused.Count));
        }

        shell.WriteLine(Loc.GetString("cmd-wg_stasis-states",
            ("awake", counts[(int) WGGridStasisState.Awake]),
            ("pausing", counts[(int) WGGridStasisState.Pausing]),
            ("paused", counts[(int) WGGridStasisState.Paused]),
            ("waking", counts[(int) WGGridStasisState.Waking]),
            ("excluded", excluded),
            ("pinned", pinned)));

        shell.WriteLine(Loc.GetString("cmd-wg_stasis-pressure",
            ("pressure", _pressure.Pressure.ToString("F2")), ("idle", (int) _stasis.EffectiveIdle)));

        shell.WriteLine(Loc.GetString("cmd-wg_stasis-largest"));
        asleep.Sort(static (a, b) => b.Paused.CompareTo(a.Paused));
        for (var i = 0; i < Math.Min(Rows, asleep.Count); i++)
        {
            var (grid, paused) = asleep[i];
            shell.WriteLine($"  {paused,8} {EntityManager.GetNetEntity(grid)} {EntityManager.ToPrettyString(grid)}");
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(["wake", "pin", "unpin"], Loc.GetString("cmd-wg_stasis-hint"))
            : CompletionResult.Empty;
    }
}
