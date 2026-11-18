using Content.Server.Administration;
using Content.Shared._WG.Wilderness;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._WG.Wilderness;

/// <summary>
/// Shows the zone and Wilderness marks of the admin's own person and the ship they're aboard, and sets or clears the
/// mark on either.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class WGZoneCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WGZoneSystem _zones = default!;

    public override string Command => "wg_zone";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { } mob)
        {
            shell.WriteError(Loc.GetString("cmd-wg_zone-no-body"));
            return;
        }

        var ship = _zones.ShipOf(mob);
        var carrier = mob;
        if (args.Length > 1 && args[1] == "ship")
        {
            if (ship is not { } aboard)
            {
                shell.WriteError(Loc.GetString("cmd-wg_zone-no-ship"));
                return;
            }

            carrier = aboard;
        }

        switch (args.Length == 0 ? "info" : args[0])
        {
            case "info":
                var zone = EntityManager.TryGetComponent<WGInZoneComponent>(mob, out var inZone) ? inZone.Zone.Id : "-";
                var shipMark = ship is { } vessel
                    ? Loc.GetString("cmd-wg_zone-ship", ("ship", EntityManager.ToPrettyString(vessel)), ("mark", MarkOf(vessel)))
                    : "-";
                shell.WriteLine(Loc.GetString("cmd-wg_zone-info", ("zone", zone), ("mark", MarkOf(mob)), ("ship", shipMark)));
                break;
            case "mark":
                _zones.Mark(carrier);
                shell.WriteLine(Loc.GetString("cmd-wg_zone-marked", ("carrier", EntityManager.ToPrettyString(carrier))));
                break;
            case "clear":
                EntityManager.RemoveComponent<WGSkullComponent>(carrier);
                EntityManager.RemoveComponent<WGWildernessDwellComponent>(carrier);
                shell.WriteLine(Loc.GetString("cmd-wg_zone-cleared", ("carrier", EntityManager.ToPrettyString(carrier))));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    /// <summary>A mark's time left, "held" in a Wilderness, or "-" for none.</summary>
    private string MarkOf(EntityUid carrier)
    {
        if (!EntityManager.TryGetComponent<WGSkullComponent>(carrier, out var skull))
            return "-";

        return skull.ExpiresAt is { } at ? WGZoneText.Clock(at - _timing.CurTime) : Loc.GetString("cmd-wg_zone-held");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(new[] { "info", "mark", "clear" }, Loc.GetString("cmd-wg_zone-hint")),
            2 when args[0] is "mark" or "clear" => CompletionResult.FromHintOptions(new[] { "ship" }, Loc.GetString("cmd-wg_zone-ship-hint")),
            _ => CompletionResult.Empty,
        };
    }
}
