using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetCracker.Anchors;
using Content.Server._WF.PlanetCracker.Chunk;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server._WF.PlanetCracker.Fissures;
using Content.Server._WF.PlanetCracker.Mining;
using Content.Server._WF.PlanetCracker.Testing;
using Content.Server.Administration;
using Content.Shared._WF.Administration;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Fissures;
using Content.Shared._WF.PlanetCracker.Mining;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Commands;

/// <summary>
/// Spawns the code-built planet cracker test grids next to the calling admin, and drives one hull's crack by hand:
/// the state field, the two completions, the anchor disconnect and the fall.
/// Design D23 leaves no in-round way out of a running cut, so `state` is the only escape hatch there is.
/// </summary>
[AdminCommand(AdminFlags.Spawn | AdminFlags.Mapping)]
public sealed partial class WFCrackerCommand : LocalizedEntityCommands
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedWFSurveySystem _survey = default!;
    [Dependency] private WFCrackerSystem _crackers = default!;
    [Dependency] private WFCrackMinerSystem _miners = default!;
    [Dependency] private WFFissureSpawnerSystem _fissures = default!;
    [Dependency] private WFGravityAnchorSystem _anchors = default!;
    [Dependency] private WFPlanetChunkSystem _chunks = default!;
    [Dependency] private WFTestGridFactory _factory = default!;

    private const string SubSpawn = "spawn";
    private const string SubState = "state";
    private const string SubBegin = "begin";
    private const string SubComplete = "complete";
    private const string SubFissure = "fissure";
    private const string SubDisconnect = "disconnect";
    private const string SubReArm = "rearm";
    private const string SubRelease = "release";
    private const string SubFall = "fall";
    private const string SubExtract = "extract";
    private const string SubDrop = "drop";
    private const string SubVeins = "veins";
    private const string SubMine = "mine";

    /// <summary>The miner the `mine` subcommand plants, the cell-carrying one rather than the Empty variant.</summary>
    private const string MinerProto = "WFCrackMiner";

    private const string KindCracker = "cracker";
    private const string KindTransport = "transport";

    private const string TargetCrack = "crack";
    private const string TargetDrill = "drill";

    private const string TargetRing = "ring";
    private const string TargetSurge = "surge";

    private static readonly string[] Subcommands =
    {
        SubSpawn, SubState, SubBegin, SubComplete, SubFissure, SubDisconnect, SubReArm, SubRelease, SubFall, SubExtract,
        SubDrop, SubVeins, SubMine,
    };

    private static readonly string[] Kinds = { KindCracker, KindTransport };

    private static readonly string[] Targets = { TargetCrack, TargetDrill };

    /// <summary>`begin` accepts drill and nothing else, so its completion must not offer crack.</summary>
    private static readonly string[] BeginTargets = { TargetDrill };

    private static readonly string[] FissureTargets = { TargetRing, TargetSurge };

    /// <summary>Offset from the caller to the cracker hull, so it does not land on their head.</summary>
    private static readonly Vector2 CrackerOffset = new(8f, 8f);

    /// <summary>Offset from the caller to the transport hull, clear of the cracker.</summary>
    private static readonly Vector2 TransportOffset = new(8f, -12f);

    /// <inheritdoc/>
    public override string Command => WolfgateAdminCommands.Cracker;

    /// <inheritdoc/>
    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0 || !Subcommands.Contains(args[0]))
        {
            Reject(shell);
            return;
        }

        // The arity is per subcommand: two of the five take no argument at all, and a single blanket length check
        // would refuse `fall` outright and let `state Cracking` fall through into the wrong error.
        switch (args[0])
        {
            case SubSpawn when args.Length == 2:
                ExecuteSpawn(shell, args[1]);
                return;
            case SubState when args.Length == 2:
                ExecuteState(shell, args[1]);
                return;
            case SubBegin when args.Length == 2:
                ExecuteBegin(shell, args[1]);
                return;
            case SubComplete when args.Length == 2:
                ExecuteComplete(shell, args[1]);
                return;
            case SubFissure when args.Length == 2:
                ExecuteFissure(shell, args[1]);
                return;
            case SubDisconnect when args.Length == 1:
                ExecuteDisconnect(shell);
                return;
            case SubReArm when args.Length == 1:
                ExecuteReArm(shell);
                return;
            case SubRelease when args.Length == 1:
                ExecuteRelease(shell);
                return;
            case SubFall when args.Length == 1:
                ExecuteFall(shell);
                return;
            case SubExtract when args.Length == 1:
                ExecuteExtract(shell);
                return;
            case SubDrop when args.Length == 1:
                ExecuteDrop(shell);
                return;
            case SubVeins when args.Length == 1:
                ExecuteVeins(shell);
                return;
            case SubMine when args.Length == 1:
                ExecuteMine(shell);
                return;
            default:
                Reject(shell);
                return;
        }
    }

    /// <summary>Builds one of the code-built test hulls beside the caller.</summary>
    private void ExecuteSpawn(IConsoleShell shell, string kind)
    {
        if (!TryGetPlayerPosition(shell, out var map, out var position))
            return;

        EntityUid grid;

        switch (kind)
        {
            case KindCracker:
                // A cracker never arrives alone: the transport is built beside it and docked on, as a bought one is.
                var pair = _factory.BuildCrackerWithTransport(map, position + CrackerOffset);

                shell.WriteLine(Loc.GetString("cmd-wfcracker-spawned-with-transport",
                    ("cracker", EntityManager.ToPrettyString(pair.Cracker).ToString()),
                    ("transport", EntityManager.ToPrettyString(pair.Transport).ToString()),
                    ("map", map.ToString())));
                return;
            case KindTransport:
                grid = _factory.BuildTransport(map, position + TransportOffset);
                break;
            default:
                shell.WriteError(Loc.GetString("cmd-wfcracker-unknown-kind", ("kind", kind)));
                return;
        }

        shell.WriteLine(Loc.GetString("cmd-wfcracker-spawned",
            ("kind", kind),
            ("grid", EntityManager.ToPrettyString(grid).ToString()),
            ("map", map.ToString())));
    }

    /// <summary>Forces the hull's crack stage, the one escape hatch from a cut that cannot otherwise be called off.</summary>
    private void ExecuteState(IConsoleShell shell, string name)
    {
        if (!TryGetCracker(shell, out var cracker))
            return;

        if (!Enum.TryParse<WFCrackState>(name, true, out var state))
        {
            shell.WriteError(Loc.GetString("cmd-wfcracker-unknown-state", ("state", name)));
            return;
        }

        _crackers.SetState(cracker, state);
        Report(shell, "cmd-wfcracker-state-set", cracker.Owner, state);
    }

    /// <summary>
    /// Starts both anchors' drills, which is the only route to WFAnchorDrillStartedEvent outside the anchor verb.
    /// Without it there is no way to watch a site's fissure rings spread by hand: every other admin route jumps
    /// straight to the lock.
    /// </summary>
    private void ExecuteBegin(IConsoleShell shell, string target)
    {
        if (!TryGetCracker(shell, out var cracker))
            return;

        switch (target)
        {
            case TargetDrill:
                if (!TryGetPair(cracker, out var a, out var b))
                {
                    shell.WriteError(Loc.GetString("cmd-wfcracker-no-cracker"));
                    return;
                }

                _anchors.BeginDrill(a);
                _anchors.BeginDrill(b);

                shell.WriteLine(Loc.GetString("cmd-wfcracker-began-drill",
                    ("grid", EntityManager.ToPrettyString(cracker.Owner).ToString())));
                return;

            default:
                Reject(shell);
                return;
        }
    }

    /// <summary>Spreads a fissure ring on each anchor now, or runs the extraction surge without waiting for a cut.</summary>
    private void ExecuteFissure(IConsoleShell shell, string target)
    {
        if (!TryGetCracker(shell, out var cracker))
            return;

        switch (target)
        {
            case TargetRing:
                if (!TryGetPair(cracker, out var a, out var b))
                {
                    shell.WriteError(Loc.GetString("cmd-wfcracker-no-cracker"));
                    return;
                }

                var carriers = 0;
                var spread = 0;

                if (EntityManager.TryGetComponent<WFFissureSpawnerComponent>(a.Owner, out var spawnerA))
                {
                    carriers++;

                    if (_fissures.ForceRing((a.Owner, spawnerA)))
                        spread++;
                }

                if (EntityManager.TryGetComponent<WFFissureSpawnerComponent>(b.Owner, out var spawnerB))
                {
                    carriers++;

                    if (_fissures.ForceRing((b.Owner, spawnerB)))
                        spread++;
                }

                if (carriers == 0)
                {
                    shell.WriteError(Loc.GetString("cmd-wfcracker-no-fissures"));
                    return;
                }

                shell.WriteLine(Loc.GetString("cmd-wfcracker-fissured", ("count", spread)));
                return;

            case TargetSurge:
                shell.WriteLine(Loc.GetString("cmd-wfcracker-surged",
                    ("count", _fissures.ForceSurge(cracker.Owner))));
                return;

            default:
                Reject(shell);
                return;
        }
    }

    /// <summary>Finishes either the running cut or both anchors' drills at once.</summary>
    private void ExecuteComplete(IConsoleShell shell, string target)
    {
        if (!TryGetCracker(shell, out var cracker))
            return;

        switch (target)
        {
            case TargetCrack:
                _crackers.CompleteCrack(cracker);
                shell.WriteLine(Loc.GetString("cmd-wfcracker-completed",
                    ("grid", EntityManager.ToPrettyString(cracker.Owner).ToString())));
                return;

            case TargetDrill:
                if (!TryGetPair(cracker, out var a, out var b))
                {
                    shell.WriteError(Loc.GetString("cmd-wfcracker-no-cracker"));
                    return;
                }

                _anchors.CompleteDrill(a);
                _anchors.CompleteDrill(b);
                shell.WriteLine(Loc.GetString("cmd-wfcracker-drilled",
                    ("grid", EntityManager.ToPrettyString(cracker.Owner).ToString())));
                return;

            default:
                Reject(shell);
                return;
        }
    }

    /// <summary>Switches both of the hull's anchors off, bypassing the cancellable attempt a later feature may veto.</summary>
    private void ExecuteDisconnect(IConsoleShell shell)
    {
        if (!TryGetCracker(shell, out var cracker))
            return;

        if (!TryGetPair(cracker, out var a, out var b))
        {
            shell.WriteError(Loc.GetString("cmd-wfcracker-no-cracker"));
            return;
        }

        _anchors.ForceSwitchOff(a);
        _anchors.ForceSwitchOff(b);

        shell.WriteLine(Loc.GetString("cmd-wfcracker-disconnected",
            ("grid", EntityManager.ToPrettyString(cracker.Owner).ToString())));
    }

    /// <summary>
    /// Puts both anchors back to Locked and drops any armed pairing window with them.
    /// Off has no player-facing exit at all, so this is the only way back from a half-finished disconnect by hand.
    /// </summary>
    private void ExecuteReArm(IConsoleShell shell)
    {
        if (!TryGetCracker(shell, out var cracker))
            return;

        if (!TryGetPair(cracker, out var a, out var b))
        {
            shell.WriteError(Loc.GetString("cmd-wfcracker-no-cracker"));
            return;
        }

        // Both halves are re-armed whichever one is off; ReArm is a no-op on an anchor that is not Off.
        _anchors.ReArm(a);
        _anchors.ReArm(b);
        _crackers.ReArmWindow(cracker);

        shell.WriteLine(Loc.GetString("cmd-wfcracker-rearmed",
            ("grid", EntityManager.ToPrettyString(cracker.Owner).ToString())));
    }

    /// <summary>Lets the chunk go now rather than waiting the evacuation out.</summary>
    private void ExecuteRelease(IConsoleShell shell)
    {
        if (!TryGetCracker(shell, out var cracker))
            return;

        _crackers.ReleaseNow(cracker);

        shell.WriteLine(Loc.GetString("cmd-wfcracker-released",
            ("grid", EntityManager.ToPrettyString(cracker.Owner).ToString())));
    }

    /// <summary>Drops the hull down the planet's z-stack the same way an expired grace timer would.</summary>
    private void ExecuteFall(IConsoleShell shell)
    {
        if (!TryGetCracker(shell, out var cracker))
            return;

        _crackers.Fall(cracker);

        // Fall sets the stage itself, so the reply is the stage line rather than a message of its own.
        Report(shell, "cmd-wfcracker-state-set", cracker.Owner, cracker.Comp.State);
    }

    /// <summary>
    /// Cuts the chunk out by hand.
    /// Deliberately not `complete crack`: this is the escape hatch for a hull forced into Cracked with `wfcracker
    /// state`, which never raised the extraction hook at all.
    /// </summary>
    private void ExecuteExtract(IConsoleShell shell)
    {
        if (!TryGetCracker(shell, out var cracker))
            return;

        if (!_crackers.TryGetTargetedPair(cracker, out var a, out var b))
        {
            shell.WriteError(Loc.GetString("cmd-wfcracker-extract-failed", ("reason", "no targeted anchor pair")));
            return;
        }

        if (!_crackers.TryGetCircle(a.Owner, b.Owner, out var centre, out var radius))
        {
            shell.WriteError(Loc.GetString("cmd-wfcracker-extract-failed", ("reason", "the pair has no cut circle")));
            return;
        }

        var groundMap = EntityManager.GetComponent<TransformComponent>(a.Owner).MapUid ?? EntityUid.Invalid;

        if (!_chunks.TryExtract(cracker, a.Owner, b.Owner, centre, radius, groundMap, out var chunk))
        {
            shell.WriteError(Loc.GetString("cmd-wfcracker-extract-failed", ("reason", "see the server log")));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wfcracker-extracted",
            ("grid", EntityManager.ToPrettyString(chunk).ToString())));
    }

    /// <summary>Pushes the hull's chunk into transit the same way the watchdog would.</summary>
    private void ExecuteDrop(IConsoleShell shell)
    {
        if (!TryGetCracker(shell, out var cracker))
            return;

        if (!_chunks.TryGetChunk(cracker, out var chunk))
        {
            shell.WriteError(Loc.GetString("cmd-wfcracker-no-chunk"));
            return;
        }

        _chunks.DropChunk(chunk);

        shell.WriteLine(Loc.GetString("cmd-wfcracker-dropped",
            ("grid", EntityManager.ToPrettyString(chunk.Owner).ToString())));
    }

    /// <summary>Lists every seam the hull's chunk carries, what is left in it and whether a miner is already on it.</summary>
    private void ExecuteVeins(IConsoleShell shell)
    {
        if (!TryGetChunkGrid(shell, out var grid))
            return;

        var veins = CollectVeins(grid);

        if (veins.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-wfcracker-veins-none"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wfcracker-veins-header",
            ("count", veins.Count),
            ("grid", EntityManager.ToPrettyString(grid.Owner).ToString())));

        foreach (var (vein, idx) in veins)
        {
            shell.WriteLine(Loc.GetString("cmd-wfcracker-veins-row",
                ("vein", EntityManager.ToPrettyString(vein.Owner).ToString()),
                ("ore", _survey.GetOreName(vein.Comp.Ore)),
                ("remaining", vein.Comp.Remaining),
                ("total", vein.Comp.TotalYield),
                ("miner", TileHasMiner(grid, idx).ToString())));
        }
    }

    /// <summary>Plants an anchored crack miner on the hull's first free live seam.</summary>
    private void ExecuteMine(IConsoleShell shell)
    {
        if (!TryGetChunkGrid(shell, out var grid))
            return;

        foreach (var (candidate, idx) in CollectVeins(grid))
        {
            if (candidate.Comp.Remaining <= 0 || TileHasMiner(grid, idx))
                continue;

            // Re-resolved through the miner's own lookup rather than reported off the child walk, so the line the
            // command prints names exactly the vein the machine will read on its first tick.
            if (!_miners.TryGetVeinAt(grid, idx, out var vein))
                continue;

            var coords = _map.GridTileToLocal(grid.Owner, grid.Comp, idx);
            var miner = EntityManager.SpawnAtPosition(MinerProto, coords);

            // The prototype spawns UNANCHORED (Transform anchored: false), so this is doing real work rather than
            // re-asserting a stance the spawn already had.
            _transform.AnchorEntity(
                (miner, EntityManager.GetComponent<TransformComponent>(miner)),
                grid,
                idx);

            shell.WriteLine(Loc.GetString("cmd-wfcracker-mine-placed",
                ("miner", EntityManager.ToPrettyString(miner).ToString()),
                ("vein", EntityManager.ToPrettyString(vein.Owner).ToString()),
                ("ore", _survey.GetOreName(vein.Comp.Ore)),
                ("remaining", vein.Comp.Remaining)));

            return;
        }

        shell.WriteError(Loc.GetString("cmd-wfcracker-mine-no-vein"));
    }

    /// <summary>The hull's chunk as a grid, which both mining subcommands need before they can look at anything.</summary>
    private bool TryGetChunkGrid(IConsoleShell shell, out Entity<MapGridComponent> grid)
    {
        grid = default;

        if (!TryGetCracker(shell, out var cracker))
            return false;

        if (!_chunks.TryGetChunk(cracker, out var chunk))
        {
            shell.WriteError(Loc.GetString("cmd-wfcracker-no-chunk"));
            return false;
        }

        if (!EntityManager.TryGetComponent<MapGridComponent>(chunk.Owner, out var mapGrid))
        {
            shell.WriteError(Loc.GetString("cmd-wfcracker-no-chunk"));
            return false;
        }

        grid = (chunk.Owner, mapGrid);
        return true;
    }

    /// <summary>Every deep vein parented to a chunk grid, each with the tile index its snap cell sits at.</summary>
    private List<(Entity<WFDeepVeinComponent> Vein, Vector2i Index)> CollectVeins(Entity<MapGridComponent> grid)
    {
        var veins = new List<(Entity<WFDeepVeinComponent>, Vector2i)>();
        var children = EntityManager.GetComponent<TransformComponent>(grid.Owner).ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            if (!EntityManager.TryGetComponent<WFDeepVeinComponent>(child, out var vein))
                continue;

            var xform = EntityManager.GetComponent<TransformComponent>(child);
            veins.Add(((child, vein), _map.TileIndicesFor(grid.Owner, grid.Comp, xform.Coordinates)));
        }

        return veins;
    }

    /// <summary>Whether a crack miner already occupies a tile's snap cell, the same cell the vein lives in.</summary>
    private bool TileHasMiner(Entity<MapGridComponent> grid, Vector2i idx)
    {
        var enumerator = _map.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, idx);

        while (enumerator.MoveNext(out var other))
        {
            if (EntityManager.HasComponent<WFCrackMinerComponent>(other.Value))
                return true;
        }

        return false;
    }

    /// <summary>The usage refusal, shared by a bad subcommand, a bad arity and a bad completion target.</summary>
    private void Reject(IConsoleShell shell)
    {
        shell.WriteError(Loc.GetString("cmd-wfcracker-invalid-args"));
        shell.WriteLine(Help);
    }

    /// <summary>Writes one grid-and-stage reply.</summary>
    private void Report(IConsoleShell shell, string key, EntityUid grid, WFCrackState state)
    {
        shell.WriteLine(Loc.GetString(key,
            ("grid", EntityManager.ToPrettyString(grid).ToString()),
            ("state", state.ToString())));
    }

    /// <summary>Resolves the map and world position the calling player is standing at.</summary>
    private bool TryGetPlayerPosition(IConsoleShell shell, out MapId map, out Vector2 position)
    {
        map = MapId.Nullspace;
        position = Vector2.Zero;

        if (shell.Player?.AttachedEntity is not { Valid: true } player)
        {
            shell.WriteError(Loc.GetString("cmd-wfcracker-no-map"));
            return false;
        }

        var xform = EntityManager.GetComponent<TransformComponent>(player);

        if (xform.MapID == MapId.Nullspace)
        {
            shell.WriteError(Loc.GetString("cmd-wfcracker-no-map"));
            return false;
        }

        map = xform.MapID;
        position = _transform.GetWorldPosition(xform);
        return true;
    }

    /// <summary>
    /// The cracker hull the caller is standing on.
    /// A server console has no body and so no grid, so it falls back to the only cracker in the world - which is what a
    /// headless run or a test has. Two or more is ambiguous and is refused rather than guessed at.
    /// </summary>
    private bool TryGetCracker(IConsoleShell shell, out Entity<WFPlanetCrackerComponent> cracker)
    {
        cracker = default;

        if (shell.Player?.AttachedEntity is { Valid: true } player
            && EntityManager.GetComponent<TransformComponent>(player).GridUid is { } grid
            && EntityManager.TryGetComponent<WFPlanetCrackerComponent>(grid, out var standing))
        {
            cracker = (grid, standing);
            return true;
        }

        if (shell.Player is null)
        {
            var count = 0;
            var query = EntityManager.AllEntityQueryEnumerator<WFPlanetCrackerComponent>();

            while (query.MoveNext(out var uid, out var comp))
            {
                count++;

                if (count > 1)
                    break;

                cracker = (uid, comp);
            }

            if (count == 1)
                return true;

            cracker = default;
        }

        shell.WriteError(Loc.GetString("cmd-wfcracker-no-cracker"));
        return false;
    }

    /// <summary>The pair the anchor subcommands act on: the targeted one if there is one, otherwise the owned one.</summary>
    private bool TryGetPair(
        Entity<WFPlanetCrackerComponent> cracker,
        out Entity<WFGravityAnchorComponent> a,
        out Entity<WFGravityAnchorComponent> b)
    {
        if (_crackers.TryGetTargetedPair(cracker, out a, out b))
            return true;

        return _crackers.TryGetOwnedPair(cracker, out a, out b, false);
    }

    /// <inheritdoc/>
    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wfcracker-hint-sub"));
            case 2:
                switch (args[0])
                {
                    case SubSpawn:
                        return CompletionResult.FromHintOptions(Kinds, Loc.GetString("cmd-wfcracker-hint-kind"));
                    case SubState:
                        return CompletionResult.FromHintOptions(
                            Enum.GetNames<WFCrackState>(),
                            Loc.GetString("cmd-wfcracker-hint-state"));
                    case SubBegin:
                        return CompletionResult.FromHintOptions(
                            BeginTargets,
                            Loc.GetString("cmd-wfcracker-hint-begin"));
                    case SubComplete:
                        return CompletionResult.FromHintOptions(Targets, Loc.GetString("cmd-wfcracker-hint-target"));
                    case SubFissure:
                        return CompletionResult.FromHintOptions(
                            FissureTargets,
                            Loc.GetString("cmd-wfcracker-hint-fissure"));
                    default:
                        return CompletionResult.Empty;
                }
            default:
                return CompletionResult.Empty;
        }
    }
}
