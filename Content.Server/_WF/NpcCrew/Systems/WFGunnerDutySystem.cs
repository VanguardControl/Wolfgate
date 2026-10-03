using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.HTN;
using Content.Server.NPC.HTN;
using Content.Server.Power.EntitySystems;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Runs ship targeting only while a living gunner operates an available console.</summary>
public sealed partial class WFGunnerDutySystem : EntitySystem
{
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private Robust.Shared.Random.IRobustRandom _skillRandom = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFCrewSecuritySystem _security = default!;
    [Dependency] private WFCrewEscortSystem _escorts = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private ShipTargetingSystem _targeting = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;

    public const string ConsoleKey = "WFCrewGunneryConsole";
    public const string CoordinatesKey = "WFCrewGunneryCoordinates";

    private static readonly TimeSpan SelectionInterval = TimeSpan.FromSeconds(0.25);
    private readonly Dictionary<EntityUid, EntityUid> _occupants = new();
    private readonly Dictionary<EntityUid, (TimeSpan Next, EntityUid? Target)> _selections = new();
    private readonly HashSet<EntityUid> _driven = new();

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(ShipTargetingSystem));
    }

    /// <summary>Chooses the nearest powered, unoccupied gunnery console on the crewman's grid.</summary>
    public bool TryFindConsole(EntityUid mob, out EntityUid console)
    {
        console = default;
        if (Transform(mob).GridUid is not { } grid)
            return false;
        var best = float.MaxValue;
        var query = EntityQueryEnumerator<FireControlConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (!Usable(uid, grid, mob))
                continue;
            var distance = (_transform.GetWorldPosition(transform) - _transform.GetWorldPosition(mob)).LengthSquared();
            if (distance >= best)
                continue;
            best = distance;
            console = uid;
        }
        return best < float.MaxValue;
    }

    private bool Usable(EntityUid console, EntityUid grid, EntityUid mob)
    {
        if (TerminatingOrDeleted(console) || !HasComp<FireControlConsoleComponent>(console)
            || Transform(console).GridUid != grid || !Transform(console).Anchored || !_power.IsPowered(console))
            return false;
        // Only an authorized operator at the screen takes the console from the gunner.
        foreach (var actor in _ui.GetActors(console, FireControlConsoleUiKey.Key))
        {
            if (actor != mob && (_crew.SameCrew(mob, actor) || _security.IsAuthorized(mob, actor)))
                return false;
        }
        if (!_occupants.TryGetValue(console, out var other) || other == mob)
            return true;
        if (!TerminatingOrDeleted(other) && TryComp<WFGunnerDutyComponent>(other, out var duty)
            && duty.AtConsole && duty.Console == console)
            return false;
        _occupants.Remove(console);
        return true;
    }

    /// <summary>Takes a console after walking within normal interaction range.</summary>
    public bool TryTakeConsole(EntityUid mob, EntityUid console)
    {
        if (!TryComp<WFGunnerDutyComponent>(mob, out var duty) || Transform(mob).GridUid is not { } grid
            || !_mobs.IsAlive(mob) || HasComp<ActorComponent>(mob) || !Usable(console, grid, mob)
            || !_interaction.InRangeUnobstructed(mob, console))
            return false;
        if (duty.AtConsole && duty.Console is { } previous && previous != console
            && _occupants.TryGetValue(previous, out var holder) && holder == mob)
            _occupants.Remove(previous);
        duty.Console = console;
        duty.AtConsole = true;
        _occupants[console] = mob;
        EntityManager.System<WFCrewRoutineSystem>().Face(mob, console);
        EntityManager.System<WFCrewSpeechSystem>().Say(mob, "gunnery");
        return true;
    }

    /// <summary>Stops all targeting owned by this gunner and releases the console.</summary>
    public void Release(EntityUid mob)
    {
        _targeting.Stop(mob);
        _selections.Remove(mob);
        if (!TryComp<WFGunnerDutyComponent>(mob, out var duty))
            return;
        duty.AtConsole = false;
        if (duty.Console is { } console && _occupants.TryGetValue(console, out var holder) && holder == mob)
            _occupants.Remove(console);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        _driven.Clear();
        var query = EntityQueryEnumerator<WFGunnerDutyComponent, WFCrewComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var duty, out var crew, out var htn))
        {
            if (!duty.AtConsole)
            {
                _targeting.Stop(uid);
                _selections.Remove(uid);
                continue;
            }
            if (crew.Duty != WFCrewDuties.Gunnery || !htn.Enabled || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid)
                || duty.Console is not { } console || Transform(uid).GridUid is not { } grid
                || !Usable(console, grid, uid) || !_interaction.InRangeUnobstructed(uid, console)
                || htn.Plan is { } plan && !plan.Tasks.Any(task => task.Operator is WFTakeGunneryOperator))
            {
                Release(uid);
                continue;
            }
            // Target selection runs four times a second; the chosen ship is kept in between while still engageable.
            var here = _transform.GetMapCoordinates(grid);
            if (!_selections.TryGetValue(uid, out var selection) || now >= selection.Next
                || selection.Target is { } kept
                    && Engageable(uid, grid, crew, duty, here, kept, _alerts.IsHostileShip(grid, crew.Group, kept)) == null)
            {
                selection = (now + SelectionInterval, SelectTarget(uid, grid, crew, duty, here));
                _selections[uid] = selection;
            }
            if (selection.Target is { } hostile && _driven.Add(grid))
            {
                // A less skilled gunner lays the guns off the target and leads it poorly.
                var skill = WFCrewSkills.Of(crew.Skill);
                if (_timing.CurTime >= duty.NextAimError)
                {
                    duty.NextAimError = _timing.CurTime + TimeSpan.FromSeconds(3);
                    duty.AimError = skill.GunneryError > 0f
                        ? _skillRandom.NextAngle().ToVec() * _skillRandom.NextFloat(skill.GunneryError)
                        : Vector2.Zero;
                }

                if (_targeting.Target(uid, new EntityCoordinates(hostile, duty.AimError)) is { } aim)
                {
                    aim.LeadingAccuracy = skill.Leading;
                    aim.OffgridLeadingAccuracy = skill.Leading;
                }
            }
            else
                _targeting.Stop(uid);
        }
    }

    /// <summary>The nearest live threat in range: an attacker, a hostile docker, an assigned target or a hostile faction.</summary>
    private EntityUid? SelectTarget(EntityUid uid, EntityUid grid, WFCrewComponent crew, WFGunnerDutyComponent duty,
        MapCoordinates here)
    {
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        if (_objectives.AttackTarget(grid, crew.Group) is { } assigned
            && Engageable(uid, grid, crew, duty, here, assigned, _alerts.IsHostileShip(grid, crew.Group, assigned)) is { } first)
        {
            best = assigned;
            bestDistance = first;
        }
        foreach (var ship in _alerts.GetHostileShips(grid, crew.Group))
        {
            if (Engageable(uid, grid, crew, duty, here, ship, true) is not { } distance || distance >= bestDistance)
                continue;
            best = ship;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>Squared distance to a ship this gunner may fire on, or null when it must hold fire.</summary>
    private float? Engageable(EntityUid uid, EntityUid grid, WFCrewComponent crew, WFGunnerDutyComponent duty,
        MapCoordinates here, EntityUid ship, bool hostile)
    {
        if (ship == grid || TerminatingOrDeleted(ship) || _status.ShouldDisengage(grid, ship))
            return null;
        // Reported attackers stay targets even inside the formation for their attack window.
        if (!hostile && (_escorts.AreInFormation(grid, ship)
                || _factions.IsEntityFriendly(uid, ship) && !_objectives.IsAttackTarget(grid, crew.Group, ship)
                    && !_security.IsHostileDockingTarget(grid, crew.Group, ship)))
            return null;
        var there = _transform.GetMapCoordinates(ship);
        if (there.MapId != here.MapId)
            return null;
        var distance = (there.Position - here.Position).LengthSquared();
        return distance <= duty.Range * duty.Range ? distance : (float?) null;
    }
}
