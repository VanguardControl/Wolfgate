using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Administration;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Commands;

/// <summary>
/// NPC crew from the console: plan a grid, spawn the plan or one role, list, clear a group, change a duty.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed partial class WFCrewCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;

    private static readonly string[] Subcommands = { "plan", "spawn", "spawnrole", "list", "clear", "duty" };

    public override string Command => "wf_crew";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        switch (args[0])
        {
            case "plan":
                Plan(shell, args);
                break;
            case "spawn":
                SpawnPlan(shell, args);
                break;
            case "spawnrole":
                SpawnRole(shell, args);
                break;
            case "list":
                List(shell, args);
                break;
            case "clear":
                Clear(shell, args);
                break;
            case "duty":
                Duty(shell, args);
                break;
            default:
                shell.WriteError(Loc.GetString("cmd-wf_crew-unknown", ("sub", args[0])));
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wf_crew-hint-sub")),
            2 when args[0] == "spawnrole" => CompletionResult.FromHintOptions(
                _prototypes.EnumeratePrototypes<WFCrewRolePrototype>().Select(role => role.ID).Order(),
                Loc.GetString("cmd-wf_crew-hint-role")),
            2 when args[0] is "plan" or "spawn" => CompletionResult.FromHint(Loc.GetString("cmd-wf_crew-hint-grid")),
            _ => CompletionResult.Empty,
        };
    }

    /// <summary>wf_crew plan &lt;grid|here&gt; [deckhands]</summary>
    private void Plan(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!TryGrid(shell, args[1], out var grid) || !TryCount(shell, args, 2, out var deckhands))
            return;

        var plan = _planner.Plan(grid, deckhands);
        shell.WriteLine(Loc.GetString("cmd-wf_crew-plan-header",
            ("count", plan.Count),
            ("grid", EntityManager.ToPrettyString(grid))));
        foreach (var post in plan)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_crew-plan-line",
                ("kind", Loc.GetString($"wf-crew-post-{post.Kind.ToString().ToLowerInvariant()}")),
                ("role", post.Role.Id),
                ("x", post.Coordinates.X.ToString("0.0")),
                ("y", post.Coordinates.Y.ToString("0.0"))));
        }
    }

    /// <summary>wf_crew spawn &lt;grid|here&gt; [group] [deckhands]</summary>
    private void SpawnPlan(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 2 or > 4)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!TryGrid(shell, args[1], out var grid) || !TryCount(shell, args, 3, out var deckhands))
            return;

        var group = args.Length >= 3 ? args[2] : DefaultGroup(grid);
        var crew = _crew.SpawnCrew(_planner.Plan(grid, deckhands), group);
        shell.WriteLine(Loc.GetString("cmd-wf_crew-spawned", ("count", crew.Count), ("group", group)));
    }

    /// <summary>wf_crew spawnrole &lt;role&gt; [group]: at the caller, with the post there.</summary>
    private void SpawnRole(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            shell.WriteLine(Help);
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } player)
        {
            shell.WriteError(Loc.GetString("cmd-wf_crew-no-player"));
            return;
        }

        if (!TryRole(args[1], out var role))
        {
            shell.WriteError(Loc.GetString("cmd-wf_crew-unknown-role", ("role", args[1])));
            return;
        }

        var xform = EntityManager.GetComponent<TransformComponent>(player);
        var group = args.Length == 3 ? args[2] : xform.GridUid is { } grid ? DefaultGroup(grid) : "crew";
        if (_crew.SpawnCrewman(role.ID, xform.Coordinates, group) is not { } uid)
        {
            shell.WriteError(Loc.GetString("cmd-wf_crew-spawn-failed", ("role", role.ID)));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_crew-spawned-one",
            ("name", EntityManager.GetComponent<MetaDataComponent>(uid).EntityName),
            ("uid", uid),
            ("group", group)));
    }

    /// <summary>wf_crew list [group]</summary>
    private void List(IConsoleShell shell, string[] args)
    {
        var count = 0;
        var query = EntityManager.EntityQueryEnumerator<WFCrewComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var crew, out var meta))
        {
            if (args.Length >= 2 && crew.Group != args[1])
                continue;

            count++;
            shell.WriteLine(Loc.GetString("cmd-wf_crew-list-line",
                ("uid", uid),
                ("name", meta.EntityName),
                ("role", crew.Role?.Id ?? "-"),
                ("duty", crew.Duty),
                ("group", crew.Group)));
        }

        shell.WriteLine(Loc.GetString("cmd-wf_crew-list-footer", ("count", count)));
    }

    /// <summary>wf_crew clear &lt;group&gt;</summary>
    private void Clear(IConsoleShell shell, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteLine(Help);
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_crew-cleared", ("count", _crew.ClearGroup(args[1])), ("group", args[1])));
    }

    /// <summary>wf_crew duty &lt;mob&gt; &lt;duty&gt;</summary>
    private void Duty(IConsoleShell shell, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[1], out var net)
            || !EntityManager.TryGetEntity(net, out var uid)
            || !EntityManager.TryGetComponent<WFCrewComponent>(uid, out var crew))
        {
            shell.WriteError(Loc.GetString("cmd-wf_crew-not-crew", ("arg", args[1])));
            return;
        }

        _crew.SetDuty((uid.Value, crew), args[2]);
        shell.WriteLine(Loc.GetString("cmd-wf_crew-duty-set",
            ("name", EntityManager.GetComponent<MetaDataComponent>(uid.Value).EntityName),
            ("duty", args[2])));
    }

    /// <summary>A grid by entity id, or "here" for the caller's own.</summary>
    private bool TryGrid(IConsoleShell shell, string arg, out EntityUid grid)
    {
        grid = default;
        if (arg == "here")
        {
            if (shell.Player?.AttachedEntity is not { Valid: true } player
                || EntityManager.GetComponent<TransformComponent>(player).GridUid is not { } here)
            {
                shell.WriteError(Loc.GetString("cmd-wf_crew-not-on-grid"));
                return false;
            }

            grid = here;
            return true;
        }

        if (!NetEntity.TryParse(arg, out var net)
            || !EntityManager.TryGetEntity(net, out var uid)
            || !EntityManager.HasComponent<MapGridComponent>(uid))
        {
            shell.WriteError(Loc.GetString("cmd-wf_crew-not-a-grid", ("arg", arg)));
            return false;
        }

        grid = uid.Value;
        return true;
    }

    /// <summary>The optional deckhand count at an argument index, default 2.</summary>
    private bool TryCount(IConsoleShell shell, string[] args, int index, out int count)
    {
        count = 2;
        if (args.Length <= index)
            return true;

        if (int.TryParse(args[index], out count) && count >= 0)
            return true;

        shell.WriteError(Loc.GetString("cmd-wf_crew-bad-number", ("arg", args[index])));
        return false;
    }

    /// <summary>A role by id, or by its id without the WFCrew prefix, case-insensitive.</summary>
    private bool TryRole(string arg, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WFCrewRolePrototype? role)
    {
        if (_prototypes.TryIndex(arg, out role))
            return true;

        role = _prototypes.EnumeratePrototypes<WFCrewRolePrototype>()
            .FirstOrDefault(candidate => string.Equals(candidate.ID, arg, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(candidate.ID, "WFCrew" + arg, StringComparison.OrdinalIgnoreCase));
        return role != null;
    }

    private static string DefaultGroup(EntityUid grid)
    {
        return $"crew-{grid.Id}";
    }
}
