using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._Mono.Company;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// The warning and attack zones around encounter ships. A player ship that is not of the ship's company is told
/// over the radio to turn away inside the warning zone, and is fired on inside the attack zone for as long as it
/// stays there.
/// </summary>
public sealed partial class WFEncounterZoneSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private WFCrewEscortSystem _escorts = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WarnCooldown = TimeSpan.FromSeconds(45);
    private static readonly ProtoId<RadioChannelPrototype> Channel = "Traffic";
    private const int WarnLines = 3;

    private TimeSpan _next;
    private readonly HashSet<EntityUid> _crewed = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        var crewedKnown = false;
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            if (encounter.Resolution != null)
                continue;

            foreach (var ship in encounter.Ships.Values)
            {
                if (ship.WarnRange <= 0f && ship.AttackRange <= 0f || TerminatingOrDeleted(ship.Grid))
                    continue;

                if (!crewedKnown)
                {
                    FindCrewedShips();
                    crewedKnown = true;
                }

                Watch(ship);
            }
        }
    }

    /// <summary>Every grid with a living player aboard. Zones answer to ships people are flying, not to debris.</summary>
    private void FindCrewedShips()
    {
        _crewed.Clear();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                _crewed.Add(grid);
        }
    }

    private void Watch(WFEncounterShipState ship)
    {
        var here = _transform.GetMapCoordinates(ship.Grid);
        var company = CompOrNull<CompanyComponent>(ship.Grid)?.CompanyName.Id;
        var now = _timing.CurTime;
        foreach (var intruder in _crewed)
        {
            if (intruder == ship.Grid || HasComp<WFEncounterGridComponent>(intruder) && _escorts.AreInFormation(ship.Grid, intruder))
                continue;

            // The ship's own company comes and goes as it likes.
            if (!string.IsNullOrEmpty(company) && CompOrNull<CompanyComponent>(intruder)?.CompanyName.Id == company)
                continue;

            var there = _transform.GetMapCoordinates(intruder);
            if (there.MapId != here.MapId)
                continue;

            var distance = (there.Position - here.Position).Length();
            if (ship.AttackRange > 0f && distance <= ship.AttackRange)
            {
                _alerts.ReportShipThreat(ship.Grid, ship.Group, intruder);
                if (ship.Engaged.Add(intruder))
                    _encounters.TrySay(ship, Channel, Loc.GetString("wf-encounter-zone-attack", ("intruder", Name(intruder))));
            }
            else if (ship.WarnRange > 0f && distance <= ship.WarnRange
                     && (!ship.Warned.TryGetValue(intruder, out var until) || now >= until))
            {
                ship.Warned[intruder] = now + WarnCooldown;
                _encounters.TrySay(ship, Channel, Loc.GetString($"wf-encounter-zone-warn-{_random.Next(1, WarnLines + 1)}",
                    ("intruder", Name(intruder)), ("distance", (int) distance)));
            }
            else if (distance > ship.WarnRange)
            {
                // Out and back in is a fresh trespass.
                ship.Engaged.Remove(intruder);
            }
        }
    }
}
