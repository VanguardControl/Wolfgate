using System.Linq;
using System.Numerics;
using Content.Server._WF.Planets;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Administration;
using Content.Shared._WF.CCVar;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Caverns;

/// <summary>Lists caverns and their mouths, teleports to a gate and carves mouths by hand.</summary>
[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class WFCavernCommand : LocalizedEntityCommands
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCavernMouthSystem _mouths = default!;

    private const string SubList = "list";
    private const string SubTp = "tp";
    private const string SubMouths = "mouths";
    private const string SubOpen = "open";

    private const string TargetPad = "pad";
    private const string TargetMouth = "mouth";

    private static readonly string[] Subcommands = { SubList, SubTp, SubMouths, SubOpen };
    private static readonly string[] Targets = { TargetPad, TargetMouth };

    /// <inheritdoc/>
    public override string Command => WolfgateAdminCommands.Cavern;

    /// <inheritdoc/>
    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0 || !Subcommands.Contains(args[0]) || !ArgumentCountFits(args))
        {
            shell.WriteError(Loc.GetString("cmd-wfcavern-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        // Everything but the read-only listing needs caverns switched on.
        if (args[0] != SubList && !_cfg.GetCVar(CavernCVars.Caverns))
        {
            shell.WriteError(Loc.GetString("cmd-wfcavern-disabled"));
            return;
        }

        switch (args[0])
        {
            case SubList:
                ExecuteList(shell);
                break;
            case SubTp:
                ExecuteTp(shell, args[1], args.Length > 2 ? args[2] : TargetPad);
                break;
            case SubMouths:
                ExecuteMouths(shell, args[1]);
                break;
            case SubOpen:
                ExecuteOpen(shell);
                break;
        }
    }

    /// <summary>Whether a subcommand got the arguments it takes.</summary>
    private static bool ArgumentCountFits(string[] args)
    {
        return args[0] switch
        {
            SubTp => args.Length is 2 or 3 && (args.Length == 2 || Targets.Contains(args[2])),
            SubMouths => args.Length == 2,
            _ => args.Length == 1,
        };
    }

    /// <summary>One row per built network: planet, cavern map, mouths and players below.</summary>
    private void ExecuteList(IConsoleShell shell)
    {
        var rows = 0;
        var none = Loc.GetString("cmd-wfcavern-row-none");
        var query = EntityManager.EntityQueryEnumerator<WFPlanetNetworkComponent>();

        while (query.MoveNext(out var network, out var comp))
        {
            var cavern = none;
            var mouths = 0;
            var players = 0;

            if (EntityManager.TryGetComponent<WFCavernGroundComponent>(comp.GroundMap, out var ground))
            {
                cavern = EntityManager.ToPrettyString(ground.Cavern).ToString();
                mouths = ground.Mouths.Count;
                players = PlayersOn(ground.Cavern);
            }

            shell.WriteLine(Loc.GetString("cmd-wfcavern-row",
                ("planet", PlanetName(network, comp)),
                ("cavern", cavern),
                ("mouths", mouths),
                ("players", players)));
            rows++;
        }

        if (rows == 0)
            shell.WriteLine(Loc.GetString("cmd-wfcavern-empty"));
    }

    /// <summary>Moves the caller onto the gate's cavern pad, or beside the gate on the ground.</summary>
    private void ExecuteTp(IConsoleShell shell, string name, string target)
    {
        if (!TryGetGround(shell, name, out var ground))
            return;

        if (_mouths.GetGate(ground) is not { } gate)
        {
            shell.WriteError(Loc.GetString("cmd-wfcavern-no-cavern", ("planet", name)));
            return;
        }

        if (shell.Player?.AttachedEntity is not { } attached)
        {
            shell.WriteError(Loc.GetString("cmd-wfcavern-no-map"));
            return;
        }

        // The climb tile: the lip on the ground, the pad beside the landing in the cavern.
        var map = target == TargetMouth ? ground.Owner : ground.Comp.Cavern;
        var position = new Vector2(gate.ClimbTile.X + 0.5f, gate.ClimbTile.Y + 0.5f);
        _transform.SetCoordinates(attached, new EntityCoordinates(map, position));

        if (EntityManager.TryGetComponent<CEZPhysicsComponent>(attached, out var physics))
        {
            _zLevels.SetZPosition((attached, physics), 0f);
            _zLevels.SetZVelocity((attached, physics), 0f);
        }

        shell.WriteLine(Loc.GetString("cmd-wfcavern-tp-done", ("planet", name), ("target", target)));

        _adminLogger.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{shell.Player?.Name ?? "Server"} teleported to the {target} of the cavern gate on {EntityManager.ToPrettyString(ground.Owner)}");
    }

    /// <summary>Lists a planet's claimed mouths: kind, origin and climb tile.</summary>
    private void ExecuteMouths(IConsoleShell shell, string name)
    {
        if (!TryGetGround(shell, name, out var ground))
            return;

        if (ground.Comp.Mouths.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-wfcavern-empty"));
            return;
        }

        foreach (var mouth in ground.Comp.Mouths)
        {
            shell.WriteLine(Loc.GetString("cmd-wfcavern-mouth-row",
                ("kind", Loc.GetString("cmd-wfcavern-mouth-kind", ("kind", mouth.Kind.ToString().ToLowerInvariant()))),
                ("origin", mouth.Origin.ToString()),
                ("size", mouth.Size),
                ("climb", mouth.ClimbTile.ToString())));
        }
    }

    /// <summary>Carves a mouth with its hole at the caller's ground tile.</summary>
    private void ExecuteOpen(IConsoleShell shell)
    {
        if (shell.Player?.AttachedEntity is not { } attached
            || EntityManager.GetComponent<TransformComponent>(attached).MapUid is not { } mapUid)
        {
            shell.WriteError(Loc.GetString("cmd-wfcavern-no-map"));
            return;
        }

        if (!EntityManager.TryGetComponent<WFCavernGroundComponent>(mapUid, out var ground)
            || !EntityManager.TryGetComponent<MapGridComponent>(mapUid, out var grid))
        {
            shell.WriteError(Loc.GetString("cmd-wfcavern-not-ground"));
            return;
        }

        var origin = _map.TileIndicesFor(mapUid, grid, _transform.GetMapCoordinates(attached));

        if (!_mouths.TryOpenMouth((mapUid, ground), origin, out var refusal, ignore: attached))
        {
            shell.WriteError(Loc.GetString("cmd-wfcavern-open-refused", ("reason", refusal)));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wfcavern-open-done", ("origin", origin.ToString())));

        _adminLogger.Add(LogType.AdminCommands, LogImpact.High,
            $"{shell.Player?.Name ?? "Server"} carved a cavern mouth at {origin} on {EntityManager.ToPrettyString(mapUid)}");
    }

    /// <summary>Resolves a planet name to its ground, writing the error if it has no network or no cavern.</summary>
    private bool TryGetGround(IConsoleShell shell, string name, out Entity<WFCavernGroundComponent> ground)
    {
        ground = default;

        if (!TryFindNetwork(name, out var network))
        {
            shell.WriteError(Loc.GetString("cmd-wfcavern-unknown-planet", ("planet", name)));
            return false;
        }

        if (!EntityManager.TryGetComponent<WFCavernGroundComponent>(network.Comp.GroundMap, out var comp))
        {
            shell.WriteError(Loc.GetString("cmd-wfcavern-no-cavern", ("planet", name)));
            return false;
        }

        ground = (network.Comp.GroundMap, comp);
        return true;
    }

    /// <summary>Finds a built network by its sector body's name, the name it was built under, or its surface id with or without the WFSurface prefix.</summary>
    private bool TryFindNetwork(string name, out Entity<WFPlanetNetworkComponent> network)
    {
        var query = EntityManager.EntityQueryEnumerator<WFPlanetNetworkComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (!Names(uid, comp).Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            network = (uid, comp);
            return true;
        }

        network = default;
        return false;
    }

    /// <summary>Every name a network answers to: its sector body's, the one it was built under, and its surface id with and without the prefix.</summary>
    private IEnumerable<string> Names(EntityUid network, WFPlanetNetworkComponent comp)
    {
        if (comp.Planet is { } body && EntityManager.EntityExists(body))
            yield return EntityManager.GetComponent<MetaDataComponent>(body).EntityName;

        if (EntityManager.TryGetComponent<WFPlanetWeatherComponent>(network, out var weather)
            && !string.IsNullOrEmpty(weather.PlanetName))
            yield return weather.PlanetName;

        yield return comp.Surface.Id;
        yield return ShortSurface(comp.Surface.Id);
    }

    /// <summary>The name a network goes by: its sector body's, the name it was built under, or its surface.</summary>
    private string PlanetName(EntityUid network, WFPlanetNetworkComponent comp)
    {
        if (comp.Planet is { } body && EntityManager.EntityExists(body))
            return EntityManager.GetComponent<MetaDataComponent>(body).EntityName;

        if (EntityManager.TryGetComponent<WFPlanetWeatherComponent>(network, out var weather)
            && !string.IsNullOrEmpty(weather.PlanetName))
            return weather.PlanetName;

        return ShortSurface(comp.Surface.Id);
    }

    /// <summary>A surface id without its WFSurface prefix.</summary>
    private static string ShortSurface(string surface)
    {
        const string prefix = "WFSurface";
        return surface.StartsWith(prefix, StringComparison.Ordinal) ? surface[prefix.Length..] : surface;
    }

    /// <summary>How many players are attached to entities on a map.</summary>
    private int PlayersOn(EntityUid map)
    {
        var count = 0;
        var query = EntityManager.EntityQueryEnumerator<ActorComponent, TransformComponent>();

        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid == map)
                count++;
        }

        return count;
    }

    /// <inheritdoc/>
    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wfcavern-hint-sub"));
            case 2 when args[0] is SubTp or SubMouths:
                var names = new List<string>();
                var query = EntityManager.EntityQueryEnumerator<WFPlanetNetworkComponent>();
                while (query.MoveNext(out var uid, out var comp))
                {
                    if (EntityManager.HasComponent<WFCavernGroundComponent>(comp.GroundMap))
                        names.Add(PlanetName(uid, comp));
                }

                return CompletionResult.FromHintOptions(names.Order(), Loc.GetString("cmd-wfcavern-hint-planet"));
            case 3 when args[0] == SubTp:
                return CompletionResult.FromHintOptions(Targets, Loc.GetString("cmd-wfcavern-hint-target"));
            default:
                return CompletionResult.Empty;
        }
    }
}
