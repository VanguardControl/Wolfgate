using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._WG.ColdStorage;

/// <summary>Lists stored grids, and stores or restores one by hand.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class WGColdStoreCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WGColdStoreSystem _coldStore = default!;

    public override string Command => "wg_coldstore";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            List(shell);
            return;
        }

        if (args.Length != 2 || args[0] is not ("store" or "restore"))
        {
            shell.WriteError(Loc.GetString("cmd-wg_coldstore-usage"));
            return;
        }

        if (args[0] == "restore" && args[1] == "all")
        {
            var restored = 0;
            foreach (var standIn in _coldStore.Stored.Keys.ToList())
            {
                if (_coldStore.RestoreNow(standIn))
                    restored++;
            }

            shell.WriteLine(Loc.GetString("cmd-wg_coldstore-restored-all", ("count", restored)));
            return;
        }

        if (!NetEntity.TryParse(args[1], out var net) || !EntityManager.TryGetEntity(net, out var uid))
        {
            shell.WriteError(Loc.GetString("cmd-wg_coldstore-no-entity", ("entity", args[1])));
            return;
        }

        if (args[0] == "store")
        {
            if (_coldStore.TryStore(uid.Value, true, out var reason))
                shell.WriteLine(Loc.GetString("cmd-wg_coldstore-storing", ("entity", args[1])));
            else
                shell.WriteError(Loc.GetString("cmd-wg_coldstore-refused", ("entity", args[1]), ("reason", Loc.GetString(reason!))));
            return;
        }

        if (!_coldStore.Stored.ContainsKey(uid.Value))
        {
            shell.WriteError(Loc.GetString("cmd-wg_coldstore-not-stored", ("entity", args[1])));
            return;
        }

        shell.WriteLine(_coldStore.RestoreNow(uid.Value)
            ? Loc.GetString("cmd-wg_coldstore-restored", ("entity", args[1]))
            : Loc.GetString("cmd-wg_coldstore-restore-failed", ("entity", args[1])));
    }

    private void List(IConsoleShell shell)
    {
        shell.WriteLine(Loc.GetString("cmd-wg_coldstore-summary",
            ("grids", _coldStore.Stored.Count),
            ("entities", _coldStore.StoredEntities),
            ("bytes", _coldStore.Backend.TotalBytes),
            ("stores", _coldStore.Stores),
            ("restores", _coldStore.Restores),
            ("aborts", _coldStore.Aborts),
            ("failures", _coldStore.Failures),
            ("expired", _coldStore.Expired)));

        foreach (var (standIn, record) in _coldStore.Stored.OrderByDescending(pair => pair.Value.Entities))
        {
            var age = (int) (_timing.CurTime - record.StoredAt).TotalSeconds;
            var state = record.State == WGStoredState.Failed
                ? Loc.GetString("cmd-wg_coldstore-state-failed", ("tries", record.Failures), ("error", record.LastError ?? string.Empty))
                : Loc.GetString("cmd-wg_coldstore-state-stored");
            shell.WriteLine($"  {EntityManager.GetNetEntity(standIn)} {record.Entities,6} {record.Bytes,9} {age,6}s {state} {record.Name}");
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(["store", "restore"], Loc.GetString("cmd-wg_coldstore-hint"))
            : CompletionResult.Empty;
    }
}
