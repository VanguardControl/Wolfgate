using System.Linq;
using System.Numerics;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Console;
using Robust.Shared.Map;

namespace Content.Server._WF.NpcCrew.Commands;

public sealed partial class WFCrewCommand
{
    [Dependency] private WFCrewSetupSystem _setup = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private AdminVesselSpawnSystem _vessels = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const string ArenaVessel = "WFDredger";
    private const float ArenaSpacing = 200f;

    /// <summary>
    /// wf_crew arena [vessel]: three crewed ships of one battlegroup beside the caller. The lead holds, one escorts it, one docks with it.
    /// </summary>
    private void Arena(IConsoleShell shell, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteLine(Help);
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } player
            || EntityManager.GetComponent<TransformComponent>(player).MapID is var map && map == MapId.Nullspace)
        {
            shell.WriteError(Loc.GetString("cmd-wf_crew-no-player"));
            return;
        }

        var id = args.Length == 2 ? args[1] : ArenaVessel;
        if (!_prototypes.TryIndex<VesselPrototype>(id, out var vessel) || vessel.Abstract)
        {
            shell.WriteError(Loc.GetString("cmd-wf_crew-arena-unknown-vessel", ("vessel", id)));
            return;
        }

        var origin = _transform.GetWorldPosition(player);
        var ships = new (string Group, Vector2 Offset, bool Captain)[]
        {
            ("arena-lead", new Vector2(0, ArenaSpacing), true),
            ("arena-escort", new Vector2(-ArenaSpacing, 0), false),
            ("arena-dock", new Vector2(ArenaSpacing, 0), false),
        };

        var grids = new List<EntityUid>();
        foreach (var (group, offset, captain) in ships)
        {
            if (!_vessels.TrySpawnVessel(vessel, map, origin + offset, player, out var grid))
            {
                shell.WriteError(Loc.GetString("cmd-wf_crew-arena-vessel-failed", ("vessel", id)));
                return;
            }

            grids.Add(grid.Value);
            var mission = new WFCrewMission { Group = group, Callsign = group, Battlegroup = "arena" };
            if (!_setup.TrySpawn(grid.Value, _setup.Plan(grid.Value, 2, captain), mission, out var crew))
                shell.WriteError(Loc.GetString("cmd-wf_crew-arena-crew-failed", ("group", group)));

            shell.WriteLine(Loc.GetString("cmd-wf_crew-arena-ship",
                ("group", group),
                ("grid", EntityManager.GetNetEntity(grid.Value)),
                ("count", crew.Count)));
        }

        var lead = EntityManager.GetNetEntity(grids[0]);
        _objectives.SetQueue(grids[1], ships[1].Group, new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.Escort, Target = lead },
        });
        _objectives.SetQueue(grids[2], ships[2].Group, new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.Dock, Target = lead },
        });
        shell.WriteLine(Loc.GetString("cmd-wf_crew-arena-done"));
    }

    private IEnumerable<string> VesselIds()
    {
        return _prototypes.EnumeratePrototypes<VesselPrototype>()
            .Where(vessel => !vessel.Abstract)
            .Select(vessel => vessel.ID)
            .Order();
    }
}
