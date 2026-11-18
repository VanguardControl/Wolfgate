using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Ghost;
using Content.Shared._WG.AdminTech;
using Content.Shared.Actions;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.AdminTech;

/// <summary>
/// Return: the way out of a body spawned with Spawn as Outfit. The player goes back to the body they had before,
/// and an admin is then put in an observer where the spawned body stood; with no earlier body they become an
/// observer there. The spawned body is deleted.
/// </summary>
public sealed partial class WGReturnSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private GhostSystem _ghosts = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly EntProtoId Observer = "AdminObserver";
    private static readonly EntProtoId ExitEffect = "EffectFlashBluespace";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGReturnToObserverComponent, WGReturnToObserverEvent>(OnReturn);
    }

    /// <summary>The body a player's mind owns now, unless that is a ghost or there is none: what Return goes back to.</summary>
    public EntityUid? EarlierBody(ICommonSession player)
    {
        if (!_minds.TryGetMind(player, out _, out var mind) || mind.OwnedEntity is not { } owned)
            return null;

        return HasComp<GhostComponent>(owned) ? null : owned;
    }

    /// <summary>Gives a spawned body the Return action, remembering the body its player had before.</summary>
    public void Grant(EntityUid body, EntityUid? earlierBody)
    {
        var comp = EnsureComp<WGReturnToObserverComponent>(body);
        comp.PreviousBody = earlierBody;
        _actions.AddAction(body, ref comp.ActionEntity, comp.Action);
    }

    private void OnReturn(Entity<WGReturnToObserverComponent> ent, ref WGReturnToObserverEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Return(ent);
    }

    /// <summary>Takes the player out of a spawned body as Return does, and deletes the body.</summary>
    public void Return(Entity<WGReturnToObserverComponent> ent)
    {
        var coords = _transform.GetMoverCoordinates(ent);
        if (_minds.TryGetMind(ent, out var mindId, out var mind))
        {
            ICommonSession? session = null;
            var admin = mind.UserId is { } user
                        && _players.TryGetSessionById(user, out session)
                        && _admin.HasAdminFlag(session, AdminFlags.Admin);

            if (ent.Comp.PreviousBody is { } earlier && !TerminatingOrDeleted(earlier))
            {
                _minds.TransferTo(mindId, earlier, mind: mind);
                if (admin)
                    Observe(mindId, mind, session!, coords, visit: true);
            }
            else if (admin)
            {
                Observe(mindId, mind, session!, coords, visit: false);
            }
            else
            {
                _ghosts.SpawnGhost((mindId, mind), coords);
            }

            _adminLog.Add(LogType.Mind, LogImpact.Medium, $"{ToPrettyString(ent):player} used Return and left their spawned body");
        }

        Spawn(ExitEffect, coords);
        QueueDel(ent);
    }

    /// <summary>
    /// Puts a mind in a new admin observer: visiting it, as <c>aghost</c> does from a body, or owning it when
    /// there is no body to come back to.
    /// </summary>
    private void Observe(EntityUid mindId, MindComponent mind, ICommonSession player, EntityCoordinates coords, bool visit)
    {
        var observer = Spawn(Observer, coords);
        _transform.AttachToGridOrMap(observer);
        _metaData.SetEntityName(observer, visit && !string.IsNullOrWhiteSpace(mind.CharacterName) ? mind.CharacterName : player.Name);

        if (visit)
            _minds.Visit(mindId, observer, mind);
        else
            _minds.TransferTo(mindId, observer, mind: mind);

        _ghosts.SetCanReturnToBody(Comp<GhostComponent>(observer), visit);
        _ghosts.ApplyAdminOOCColor(observer, mindId);
    }
}
