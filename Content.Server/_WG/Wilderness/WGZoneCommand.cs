using System.Linq;
using Content.Server.Administration;
using Content.Shared._WG.Wilderness;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._WG.Wilderness;

/// <summary>
/// Shows the zone and Wilderness marks of a person and the ship they're aboard, or of a ship, and sets an ordinary or
/// standing mark on either, or clears it. With no target, the admin's own body.
/// </summary>
[AdminCommand(WGZoneSystem.StaffFlag)]
public sealed partial class WGZoneCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WGZoneSystem _zones = default!;

    public override string Command => "wg_zone";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var action = args.Length == 0 ? "info" : args[0];
        var wantShip = args.Skip(1).Contains("ship");
        var name = args.Skip(1).FirstOrDefault(arg => arg != "ship");

        if (!TryTarget(shell, name, out var target))
            return;

        var ship = _zones.IsShip(target) ? target : _zones.ShipOf(target);
        var carrier = ship == target ? target : wantShip ? ship : target;
        if (carrier is not { } chosen)
        {
            shell.WriteError(Loc.GetString("cmd-wg_zone-no-ship"));
            return;
        }

        switch (action)
        {
            case "info":
                var zone = EntityManager.TryGetComponent<WGInZoneComponent>(target, out var inZone) ? inZone.Zone.Id : "-";
                var shipMark = ship is { } vessel && vessel != target
                    ? Loc.GetString("cmd-wg_zone-ship", ("ship", EntityManager.ToPrettyString(vessel)), ("mark", MarkOf(vessel)))
                    : "-";
                shell.WriteLine(Loc.GetString("cmd-wg_zone-info",
                    ("target", EntityManager.ToPrettyString(target)),
                    ("zone", zone),
                    ("mark", MarkOf(target)),
                    ("ship", shipMark)));
                break;
            case "mark":
            case "standing":
                // A ship, vehicle or living body; not a station, item or ghost.
                if (chosen != ship && !_zones.CanCarry(chosen))
                {
                    shell.WriteError(Loc.GetString("cmd-wg_zone-cannot-carry", ("target", EntityManager.ToPrettyString(chosen))));
                    return;
                }

                shell.WriteLine(_zones.StaffMarkText(chosen, action == "standing"));
                break;
            case "clear":
                shell.WriteLine(_zones.StaffClearText(chosen));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    /// <summary>The target: a player's body by name, an entity by id, or with neither the admin's own body.</summary>
    private bool TryTarget(IConsoleShell shell, string? name, out EntityUid target)
    {
        target = default;
        if (name == null)
        {
            if (shell.Player?.AttachedEntity is not { } mob)
            {
                shell.WriteError(Loc.GetString("cmd-wg_zone-no-body"));
                return false;
            }

            target = mob;
            return true;
        }

        if (_players.TryGetSessionByUsername(name, out var session))
        {
            if (session.AttachedEntity is not { } body)
            {
                shell.WriteError(Loc.GetString("cmd-wg_zone-player-no-body", ("player", session.Name)));
                return false;
            }

            target = body;
            return true;
        }

        if (NetEntity.TryParse(name, out var net) && EntityManager.TryGetEntity(net, out var uid) && EntityManager.EntityExists(uid))
        {
            target = uid.Value;
            return true;
        }

        shell.WriteError(Loc.GetString("cmd-wg_zone-no-target", ("target", name)));
        return false;
    }

    /// <summary>A mark's time left, "held" in a Wilderness, "standing", or "-" for none.</summary>
    private string MarkOf(EntityUid carrier)
    {
        if (!EntityManager.TryGetComponent<WGSkullComponent>(carrier, out var skull))
            return "-";

        if (skull.Permanent)
            return Loc.GetString("cmd-wg_zone-permanent-state");

        if (skull.Staff)
            return Loc.GetString("cmd-wg_zone-standing-state");

        return skull.ExpiresAt is { } at ? WGZoneText.Clock(at - _timing.CurTime) : Loc.GetString("cmd-wg_zone-held");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(new[] { "info", "mark", "standing", "clear" }, Loc.GetString("cmd-wg_zone-hint"));

        var options = CompletionHelper.SessionNames(players: _players).Prepend(new CompletionOption("ship"));
        return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-wg_zone-target-hint"));
    }
}
