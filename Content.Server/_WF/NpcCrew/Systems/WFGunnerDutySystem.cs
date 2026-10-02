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

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Runs ship targeting only while a living gunner operates an available console.</summary>
public sealed partial class WFGunnerDutySystem : EntitySystem
{
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private ShipTargetingSystem _targeting = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public const string ConsoleKey = "WFCrewGunneryConsole";
    public const string CoordinatesKey = "WFCrewGunneryCoordinates";

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
            || Transform(console).GridUid != grid || !Transform(console).Anchored || !_power.IsPowered(console)
            || _ui.IsUiOpen(console, FireControlConsoleUiKey.Key))
            return false;
        var query = EntityQueryEnumerator<WFGunnerDutyComponent>();
        while (query.MoveNext(out var other, out var duty))
        {
            if (other != mob && duty.AtConsole && duty.Console == console)
                return false;
        }
        return true;
    }

    /// <summary>Takes a console after walking within normal interaction range.</summary>
    public bool TryTakeConsole(EntityUid mob, EntityUid console)
    {
        if (!TryComp<WFGunnerDutyComponent>(mob, out var duty) || Transform(mob).GridUid is not { } grid
            || !_mobs.IsAlive(mob) || HasComp<ActorComponent>(mob) || !Usable(console, grid, mob)
            || !_interaction.InRangeUnobstructed(mob, console))
            return false;
        duty.Console = console;
        duty.AtConsole = true;
        return true;
    }

    /// <summary>Stops all targeting owned by this gunner and releases the console.</summary>
    public void Release(EntityUid mob)
    {
        _targeting.Stop(mob);
        if (TryComp<WFGunnerDutyComponent>(mob, out var duty))
            duty.AtConsole = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var driven = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<WFGunnerDutyComponent, WFCrewComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var duty, out var crew, out var htn))
        {
            if (!duty.AtConsole)
            {
                _targeting.Stop(uid);
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
            var here = _transform.GetMapCoordinates(grid);
            var target = _alerts.GetHostileShips(grid, crew.Group)
                .Where(ship => ship != grid && !_factions.IsEntityFriendly(uid, ship)
                    && _transform.GetMapCoordinates(ship).MapId == here.MapId
                    && (_transform.GetWorldPosition(ship) - here.Position).LengthSquared() <= duty.Range * duty.Range)
                .OrderBy(ship => (_transform.GetWorldPosition(ship) - here.Position).LengthSquared())
                .Select(ship => (EntityUid?) ship).FirstOrDefault();
            if (target is { } hostile && driven.Add(grid))
                _targeting.Target(uid, new EntityCoordinates(hostile, Vector2.Zero));
            else
                _targeting.Stop(uid);
        }
    }
}
