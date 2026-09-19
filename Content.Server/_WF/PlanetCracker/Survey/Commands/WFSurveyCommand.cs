using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Shared._WF.Administration;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.PlanetCracker.Survey.Commands;

/// <summary>
/// Reads the survey instruments from the console: the sector list exactly as a survey console would draw it, the deep
/// veins around the caller, and a reveal that stands in for walking the ground with a surveyor.
/// A command name of its own rather than another wfplanet subcommand, so that command's blanket two-argument gate, its
/// subcommand list and its three enumerating locale keys all stay untouched.
/// </summary>
[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class WFSurveyCommand : LocalizedEntityCommands
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedWFSurveySystem _survey = default!;
    [Dependency] private WFSurveyConsoleSystem _consoles = default!;

    private const string SubList = "list";
    private const string SubVeins = "veins";
    private const string SubReveal = "reveal";

    /// <summary>Radius the two vein subcommands sweep when the caller names none.</summary>
    private const float DefaultRadius = 12f;

    private static readonly string[] Subcommands = { SubList, SubVeins, SubReveal };

    /// <inheritdoc/>
    public override string Command => WolfgateAdminCommands.Survey;

    /// <inheritdoc/>
    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0 || !Subcommands.Contains(args[0]))
        {
            Reject(shell);
            return;
        }

        // The arity is per subcommand: `list` takes nothing and the two vein sweeps take an optional radius, so a
        // blanket length check would refuse two of the three outright.
        switch (args[0])
        {
            case SubList when args.Length == 1:
                ExecuteList(shell);
                return;
            case SubVeins when args.Length is 1 or 2:
                ExecuteVeins(shell, args);
                return;
            case SubReveal when args.Length is 1 or 2:
                ExecuteReveal(shell, args);
                return;
            default:
                Reject(shell);
                return;
        }
    }

    /// <summary>
    /// Writes one row per star-system body, read from the same BuildState a console window would receive, so the
    /// command can never drift from what the crew is looking at.
    /// </summary>
    private void ExecuteList(IConsoleShell shell)
    {
        if (!TryGetConsole(shell, out var console))
            return;

        var state = _consoles.BuildState(console);

        if (state.Planets.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-wfsurvey-list-empty"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wfsurvey-list-header",
            ("system", state.SystemName),
            ("count", state.Planets.Count)));

        foreach (var row in state.Planets)
        {
            shell.WriteLine(Loc.GetString("cmd-wfsurvey-list-row",
                ("name", row.Name),
                ("distance", row.DistanceKnown ? row.Distance.ToString("F0", CultureInfo.InvariantCulture) : "?"),
                ("sanctioned", row.Sanctioned.ToString()),
                ("cracked", row.Cracked.ToString()),
                ("rating", row.VeinsKnown ? row.Veins.ToString() : "?"),
                ("beacon", row.HasBeacon ? row.Beacon : "-")));
        }
    }

    /// <summary>Counts and names the deep veins around the caller, ore and yield included.</summary>
    private void ExecuteVeins(IConsoleShell shell, string[] args)
    {
        if (!TryGetCaller(shell, out var caller) || !TryGetRadius(shell, args, out var radius))
            return;

        var veins = GetVeins(caller, radius);

        if (veins.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-wfsurvey-veins-none"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wfsurvey-veins-header",
            ("count", veins.Count),
            ("radius", radius.ToString("F0", CultureInfo.InvariantCulture))));

        foreach (var vein in veins)
        {
            shell.WriteLine(Loc.GetString("cmd-wfsurvey-veins-row",
                ("vein", EntityManager.ToPrettyString(vein.Owner).ToString()),
                ("ore", _survey.GetOreName(vein.Comp.Ore)),
                ("yield", vein.Comp.TotalYield),
                ("rich", vein.Comp.Rich.ToString())));
        }
    }

    /// <summary>Reveals every vein around the caller to the caller alone, exactly as a surveyor pulse would.</summary>
    private void ExecuteReveal(IConsoleShell shell, string[] args)
    {
        if (!TryGetCaller(shell, out var caller) || !TryGetRadius(shell, args, out var radius))
            return;

        var veins = GetVeins(caller, radius);
        var surveyed = EntityManager.EnsureComponent<WFSurveyedComponent>(caller);

        foreach (var vein in veins)
        {
            surveyed.Revealed.Add(EntityManager.GetNetEntity(vein.Owner));
        }

        EntityManager.Dirty(caller, surveyed);

        shell.WriteLine(Loc.GetString("cmd-wfsurvey-revealed",
            ("count", veins.Count),
            ("radius", radius.ToString("F0", CultureInfo.InvariantCulture))));
    }

    /// <summary>Every vein within radius of the caller, on the caller's own map.</summary>
    private List<Entity<WFDeepVeinComponent>> GetVeins(EntityUid caller, float radius)
    {
        var xform = EntityManager.GetComponent<TransformComponent>(caller);

        // Uncontained, matching the surveyor: an anchored bodyless vein rides the dynamic sundries tree.
        var hits = _lookup.GetEntitiesInRange<WFDeepVeinComponent>(xform.Coordinates, radius, LookupFlags.Uncontained);
        var veins = new List<Entity<WFDeepVeinComponent>>();

        foreach (var hit in hits)
        {
            if (EntityManager.GetComponent<TransformComponent>(hit.Owner).MapUid != xform.MapUid)
                continue;

            veins.Add(hit);
        }

        return veins;
    }

    /// <summary>The optional radius argument, defaulting where it is absent and refusing what will not parse.</summary>
    private bool TryGetRadius(IConsoleShell shell, string[] args, out float radius)
    {
        radius = DefaultRadius;

        if (args.Length < 2)
            return true;

        if (float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) && radius > 0f)
            return true;

        shell.WriteError(Loc.GetString("cmd-wfsurvey-bad-radius", ("radius", args[1])));
        return false;
    }

    /// <summary>The calling player's body, which is what both vein sweeps measure from.</summary>
    private bool TryGetCaller(IConsoleShell shell, out EntityUid caller)
    {
        caller = default;

        if (shell.Player?.AttachedEntity is not { Valid: true } player)
        {
            shell.WriteError(Loc.GetString("cmd-wfsurvey-no-body"));
            return false;
        }

        caller = player;
        return true;
    }

    /// <summary>
    /// A survey console to read the sector list off.
    /// The one the caller is standing on if there is one; otherwise the only one in the world, which is what a headless
    /// run or a test has. Two or more is ambiguous and is refused rather than guessed at.
    /// </summary>
    private bool TryGetConsole(IConsoleShell shell, out EntityUid console)
    {
        console = default;

        if (shell.Player?.AttachedEntity is { Valid: true } player
            && EntityManager.GetComponent<TransformComponent>(player).GridUid is { } grid)
        {
            var onGrid = EntityManager.EntityQueryEnumerator<WFSectorSurveyConsoleComponent, TransformComponent>();

            while (onGrid.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != grid)
                    continue;

                console = uid;
                return true;
            }
        }

        var count = 0;
        var query = EntityManager.AllEntityQueryEnumerator<WFSectorSurveyConsoleComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            count++;

            if (count > 1)
                break;

            console = uid;
        }

        if (count == 1)
            return true;

        console = default;
        shell.WriteError(Loc.GetString("cmd-wfsurvey-no-console"));
        return false;
    }

    /// <summary>The usage refusal, shared by a bad subcommand and a bad arity.</summary>
    private void Reject(IConsoleShell shell)
    {
        shell.WriteError(Loc.GetString("cmd-wfsurvey-invalid-args"));
        shell.WriteLine(Help);
    }

    /// <inheritdoc/>
    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wfsurvey-hint-sub"));
            case 2:
                switch (args[0])
                {
                    case SubVeins:
                    case SubReveal:
                        return CompletionResult.FromHint(Loc.GetString("cmd-wfsurvey-hint-radius"));
                    default:
                        return CompletionResult.Empty;
                }
            default:
                return CompletionResult.Empty;
        }
    }
}
