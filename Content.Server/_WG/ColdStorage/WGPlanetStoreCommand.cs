using Content.Server._WG.Planets;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._WG.ColdStorage;

/// <summary>Lists worlds with what the planet store has done to them, and unloads or loads one by hand.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class WGPlanetStoreCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WGPlanetStoreSystem _store = default!;

    public override string Command => "wg_planetstore";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            List(shell);
            return;
        }

        if (args.Length != 2 || args[0] is not ("unload" or "load"))
        {
            shell.WriteError(Loc.GetString("cmd-wg_planetstore-usage"));
            return;
        }

        if (Find(args[1]) is not { } network)
        {
            shell.WriteError(Loc.GetString("cmd-wg_planetstore-no-world", ("world", args[1])));
            return;
        }

        if (args[0] == "load")
        {
            _store.Load(network);
            shell.WriteLine(Loc.GetString("cmd-wg_planetstore-loading", ("world", args[1])));
            return;
        }

        if (_store.TryUnload(network, out var reason))
            shell.WriteLine(Loc.GetString("cmd-wg_planetstore-unloading", ("world", args[1])));
        else
            shell.WriteError(Loc.GetString("cmd-wg_planetstore-refused", ("world", args[1]), ("reason", Loc.GetString(reason!))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(new[] { "unload", "load" }, Loc.GetString("cmd-wg_planetstore-hint"));

        if (args.Length != 2)
            return CompletionResult.Empty;

        var names = new List<string>();
        var networks = EntityManager.AllEntityQueryEnumerator<WGPlanetNetworkComponent>();
        while (networks.MoveNext(out _, out var net))
        {
            names.Add(net.Surface.Id);
        }

        return CompletionResult.FromOptions(names);
    }

    private EntityUid? Find(string name)
    {
        var networks = EntityManager.AllEntityQueryEnumerator<WGPlanetNetworkComponent>();
        while (networks.MoveNext(out var uid, out var net))
        {
            if (net.Surface.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
                return uid;
        }

        return null;
    }

    private void List(IConsoleShell shell)
    {
        shell.WriteLine(Loc.GetString("cmd-wg_planetstore-summary", ("entities", _store.StoredEntities)));
        var networks = EntityManager.AllEntityQueryEnumerator<WGPlanetNetworkComponent>();
        while (networks.MoveNext(out var uid, out var net))
        {
            _store.Planets.TryGetValue(uid, out var planet);
            var occupied = _store.Occupied(uid, net, out var who);
            shell.WriteLine(Loc.GetString("cmd-wg_planetstore-row",
                ("world", net.Surface.Id),
                ("state", (planet?.State ?? WGPlanetStoreState.Loaded).ToString()),
                ("stored", planet?.State == WGPlanetStoreState.Loaded ? 0 : planet?.StoredEntities ?? 0),
                ("kept", planet?.State == WGPlanetStoreState.Loaded ? 0 : planet?.KeptEntities ?? 0),
                ("bytes", planet?.Bytes ?? 0),
                ("empty", planet?.EmptySince is { } since ? (int) (_timing.CurTime - since).TotalSeconds : 0),
                ("who", occupied && who.Length > 0 ? Loc.GetString(who) : Loc.GetString("cmd-wg_planetstore-nobody")),
                ("error", planet?.LastError ?? string.Empty)));
        }
    }
}
