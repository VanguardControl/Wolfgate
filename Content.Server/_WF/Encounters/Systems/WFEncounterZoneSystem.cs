using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Encounters;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
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
    [Dependency] private IPrototypeManager _prototypes = default!;
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
    private readonly Dictionary<EntityUid, Crewed> _crewed = new();
    private readonly List<EntityUid> _stale = new();

    /// <summary>A ship with a living player aboard.</summary>
    private sealed class Crewed
    {
        /// <summary>The companies it flies for: its own, or failing that its players'.</summary>
        public readonly List<string> Flags = new();

        /// <summary>The companies of the living players aboard.</summary>
        public readonly HashSet<string> Aboard = new();
    }

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
                // A ship lying in wait gives itself away to nobody.
                if (ship.WarnRange <= 0f && ship.AttackRange <= 0f || ship.Lurking || TerminatingOrDeleted(ship.Grid))
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

    /// <summary>Every ship with a living player aboard. Zones answer to ships people are flying, not to stations or debris.</summary>
    private void FindCrewedShips()
    {
        _crewed.Clear();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || HasComp<GhostComponent>(uid) || _mobs.IsDead(uid))
                continue;

            if (!_crewed.TryGetValue(grid, out var crewed))
            {
                if (!IsPlayerShip(grid))
                    continue;

                _crewed[grid] = crewed = new Crewed();
            }

            var own = Company(uid);
            var company = Company(grid);
            if (company.Length == 0)
                company = own;
            if (company.Length > 0 && !crewed.Flags.Contains(company))
                crewed.Flags.Add(company);
            if (own.Length > 0)
                crewed.Aboard.Add(own);
        }
    }

    /// <summary>Whether a grid is a ship people fly: not a station, outpost or asteroid, and not one of an encounter's own.</summary>
    private bool IsPlayerShip(EntityUid grid)
    {
        if (HasComp<WFEncounterGridComponent>(grid))
            return false;

        // Stations, outposts and asteroids are station members; a deed makes one a ship somebody owns.
        return HasComp<ShuttleDeedComponent>(grid) || !HasComp<StationMemberComponent>(grid);
    }

    /// <summary>Whether any port of a grid is docked to another.</summary>
    private bool IsDocked(EntityUid grid)
    {
        var docks = EntityQueryEnumerator<Content.Server.Shuttles.Components.DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid == grid && dock.DockedWith != null)
                return true;
        }

        return false;
    }

    /// <summary>Forgets intruders that left the map or the crewed ships, so a ship that comes back is told again.</summary>
    private void Forget(WFEncounterShipState ship)
    {
        if (ship.Engaged.Count == 0 && ship.Warned.Count == 0)
            return;

        var map = Transform(ship.Grid).MapID;
        _stale.Clear();
        foreach (var uid in ship.Engaged)
        {
            if (IsGone(uid, map))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            ship.Engaged.Remove(uid);
        }

        _stale.Clear();
        foreach (var uid in ship.Warned.Keys)
        {
            if (IsGone(uid, map))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            ship.Warned.Remove(uid);
        }
    }

    private bool IsGone(EntityUid intruder, MapId map)
    {
        return TerminatingOrDeleted(intruder) || !_crewed.ContainsKey(intruder) || Transform(intruder).MapID != map;
    }

    /// <summary>An entity's company id, or empty for none.</summary>
    private string Company(EntityUid uid)
    {
        return CompOrNull<CompanyComponent>(uid)?.CompanyName.Id is { } id && id != "None" ? id : string.Empty;
    }

    /// <summary>Whether two companies are at war: either one's standing lists the other.</summary>
    public bool AtWar(string first, string second)
    {
        if (first.Length == 0 || second.Length == 0 || first == second)
            return false;

        return _prototypes.TryIndex<WFStandingPrototype>(first, out var mine) && mine.AtWar.Contains(second)
            || _prototypes.TryIndex<WFStandingPrototype>(second, out var theirs) && theirs.AtWar.Contains(first);
    }

    private bool AtWar(string company, List<string> flags)
    {
        foreach (var flag in flags)
        {
            if (AtWar(company, flag))
                return true;
        }

        return false;
    }

    private void Watch(WFEncounterShipState ship)
    {
        Forget(ship);

        // A ship in port is not on guard: whatever is berthed beside it is none of its business.
        if (IsDocked(ship.Grid))
            return;

        var here = CentreOfMass(ship.Grid);
        var company = Company(ship.Grid);
        var now = _timing.CurTime;
        foreach (var (intruder, crewed) in _crewed)
        {
            // Formation partners and the ships a side has taken as allies are not intruders.
            if (intruder == ship.Grid || _escorts.AreInFormation(ship.Grid, intruder))
                continue;

            // The ship's own company comes and goes as it likes, and so does anyone flying with one of its people.
            if (company.Length > 0 && (crewed.Flags.Contains(company) || crewed.Aboard.Contains(company)))
                continue;

            // A patrol only minds its faction's enemies.
            if (ship.ZoneTargets == WFEncounterZoneTargets.AtWar && !AtWar(company, crewed.Flags))
                continue;

            var there = CentreOfMass(intruder);
            if (there.MapId != here.MapId)
                continue;

            var distance = (there.Position - here.Position).Length();
            if (ship.AttackRange > 0f && distance <= ship.AttackRange)
            {
                _alerts.ReportZoneThreat(ship.Grid, ship.Group, intruder);
                if (ship.Engaged.Add(intruder))
                    _encounters.TrySay(ship, Channel, Loc.GetString($"{ship.ZoneLines}-attack", ("intruder", Name(intruder))));
            }
            else if (ship.WarnRange > 0f && distance <= ship.WarnRange
                     && (!ship.Warned.TryGetValue(intruder, out var until) || now >= until))
            {
                ship.Warned[intruder] = now + WarnCooldown;
                _encounters.TrySay(ship, Channel, Loc.GetString($"{ship.ZoneLines}-warn-{_random.Next(1, WarnLines + 1)}",
                    ("intruder", Name(intruder)), ("distance", (int) distance)));
            }
            else if (distance > ship.WarnRange)
            {
                // Out and back in is a fresh trespass.
                ship.Engaged.Remove(intruder);
            }
        }
    }

    /// <summary>Where a hull's centre of mass is: the point the radar draws it and its zones around.</summary>
    private MapCoordinates CentreOfMass(EntityUid grid)
    {
        var centre = CompOrNull<Robust.Shared.Physics.Components.PhysicsComponent>(grid)?.LocalCenter ?? System.Numerics.Vector2.Zero;
        return _transform.ToMapCoordinates(new EntityCoordinates(grid, centre));
    }
}
