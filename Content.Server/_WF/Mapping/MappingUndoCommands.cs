using Content.Server.Administration;
using Content.Server.Popups;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.Mapping;

/// <summary>Takes back the mapper's last placements, erases or tile changes.</summary>
[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class MapUndoCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "mapundo";
    public string Description => Loc.GetString("cmd-mapundo-desc");
    public string Help => Loc.GetString("cmd-mapundo-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        MappingUndoShell.Run(_entities, shell, args, redo: false);
    }
}

/// <summary>Puts back what mapundo took away.</summary>
[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class MapRedoCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "mapredo";
    public string Description => Loc.GetString("cmd-mapredo-desc");
    public string Help => Loc.GetString("cmd-mapredo-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        MappingUndoShell.Run(_entities, shell, args, redo: true);
    }
}

/// <summary>Shared body of mapundo and mapredo: argument handling and the lines the mapper gets back.</summary>
public static class MappingUndoShell
{
    public static void Run(IEntityManager entities, IConsoleShell shell, string[] args, bool redo)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        var count = 1;
        if (args.Length > 1 || (args.Length == 1 && (!int.TryParse(args[0], out count) || count < 1)))
        {
            shell.WriteError(Loc.GetString("mapping-undo-bad-count"));
            return;
        }

        var system = entities.System<MappingUndoSystem>();
        var report = system.Apply(player.UserId, count, redo);

        if (!report.Any)
        {
            var empty = Loc.GetString(redo ? "mapping-redo-nothing" : "mapping-undo-nothing");
            shell.WriteLine(empty);
            entities.System<PopupSystem>().PopupCursor(empty, player);
            return;
        }

        var key = redo ? "mapping-redo-done" : "mapping-undo-done";
        foreach (var line in report.Lines)
        {
            shell.WriteLine(Loc.GetString(key, ("what", line)));
        }

        var notes = new List<string>();
        if (report.Skipped)
            notes.Add(Loc.GetString("mapping-undo-skipped"));

        if (report.Truncated)
            notes.Add(Loc.GetString("mapping-undo-truncated"));

        if (report.Restored)
            notes.Add(Loc.GetString("mapping-undo-links"));

        foreach (var note in notes)
        {
            shell.WriteLine(note);
        }

        var popup = Loc.GetString(key, ("what", report.Lines[0]));
        if (report.Lines.Count > 1)
            popup += " " + Loc.GetString("mapping-undo-part-more", ("count", report.Lines.Count - 1));

        entities.System<PopupSystem>().PopupCursor(popup, player);

        var (undo, redoLeft) = system.GetCounts(player.UserId);
        shell.WriteLine(Loc.GetString("mapping-undo-left", ("undo", undo), ("redo", redoLeft)));
    }
}
