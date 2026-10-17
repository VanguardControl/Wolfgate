using Content.Server._WF.Administration.Systems;
using Content.Server._WF.Audio.InternetSound;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.Hailing;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.ShipPa;
using Content.Server.Administration.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.GodPanel;
using Content.Shared._WF.ShipPa;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Follower;
using Content.Shared.Follower.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._WF.GodPanel;

public sealed partial class WFGodPanelSystem
{
    [Dependency] private AdminHailSystem _adminHail = default!;
    [Dependency] private FollowerSystem _follower = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private RejuvenateSystem _rejuvenate = default!;
    [Dependency] private ShipAlertSystem _alert = default!;
    [Dependency] private ShipPaSystem _pa = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    private const int MaxAnnouncementLength = 256;

    private void InitializeActions()
    {
        SubscribeNetworkEvent<WFGodPanelActionEvent>(OnAction);
    }

    private void OnAction(WFGodPanelActionEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        EntityUid? target = null;
        if (ev.Target is { } net && !TryGetEntity(net, out target))
        {
            _chat.DispatchServerMessage(session, Loc.GetString("wf-godpanel-error-gone"));
            return;
        }

        Act(session, ev.Action, target, ev.Text, ev.Flag, out var message);
        if (message.Length > 0)
            _chat.DispatchServerMessage(session, message);
    }

    /// <summary>The flag the tool behind an action already needs.</summary>
    public static AdminFlags RequiredFlag(WFGodAction action)
    {
        return action switch
        {
            WFGodAction.Announce
                or WFGodAction.SetCode
                or WFGodAction.GeneralQuartersOn
                or WFGodAction.GeneralQuartersOff
                or WFGodAction.DistressOn
                or WFGodAction.DistressOff
                or WFGodAction.HailAsGod
                or WFGodAction.PlayPaSound
                or WFGodAction.StopPaSound
                or WFGodAction.PlaySound
                or WFGodAction.StopSound => AdminFlags.Fun,
            _ => AdminFlags.Admin,
        };
    }

    /// <summary>
    /// Does one panel action for a subscribed admin after checking the flags, logs it, and puts it on the
    /// timeline. <paramref name="message"/> is what to tell the admin: why it failed, or what the tool answered.
    /// </summary>
    public bool Act(ICommonSession session, WFGodAction action, EntityUid? target, string? text, bool flag,
        out string message)
    {
        message = string.Empty;

        if (!_subscribers.ContainsKey(session) || !HasFlag(session, AdminFlags.Admin)
            || !HasFlag(session, RequiredFlag(action)))
        {
            _adminLog.Add(LogType.AdminCommands, LogImpact.Medium,
                $"{session.Name} sent the God Panel action {action} without the flag for it");
            return false;
        }

        if (!Do(session, action, target, text?.Trim() ?? string.Empty, flag, out message, out var subject))
            return false;

        var targetName = subject is { } uid ? Name(uid) : string.Empty;
        var gm = session.AttachedEntity is { } body ? Name(body) : session.Name;
        var detail = action is WFGodAction.Announce or WFGodAction.SetCode or WFGodAction.PlayPaSound
            or WFGodAction.PlaySound
            ? text?.Trim() ?? string.Empty
            : string.Empty;

        _adminLog.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{session.Name} used the God Panel action {action} on {(subject is { } s ? ToPrettyString(s).ToString() : "the round")} {detail}");
        AddEvent(WFGodEventCategory.Gm,
            Loc.GetString("wf-godpanel-event-gm",
                ("gm", gm),
                ("action", action.ToString().ToLowerInvariant()),
                ("target", targetName),
                ("text", detail)),
            subject,
            subject is { } at ? CoordinatesOf(at) : null);
        return true;
    }

    /// <summary>Carries the action out. Returns false with a message when it could not.</summary>
    private bool Do(ICommonSession session, WFGodAction action, EntityUid? target, string text, bool flag,
        out string message, out EntityUid? subject)
    {
        message = string.Empty;
        subject = null;

        // Everything except the round actions needs something to act on.
        var round = action is WFGodAction.PlaySound or WFGodAction.StopSound
            or WFGodAction.PauseEncounters or WFGodAction.ResumeEncounters
            or WFGodAction.ReaverPause or WFGodAction.ReaverResume
            or WFGodAction.WarLevelCold or WFGodAction.WarLevelHot;

        EntityUid thing = default;
        if (!round)
        {
            if (target is not { } found || TerminatingOrDeleted(found))
            {
                message = Loc.GetString("wf-godpanel-error-gone");
                return false;
            }

            thing = found;
            subject = found;
        }

        switch (action)
        {
            case WFGodAction.Follow:
                if (session.AttachedEntity is not { } follower || follower == thing)
                    return Fail(out message, "wf-godpanel-error-no-body");

                _follower.StartFollowingEntity(follower, thing);
                return true;

            case WFGodAction.GoTo:
                if (session.AttachedEntity is not { } visitor || visitor == thing)
                    return Fail(out message, "wf-godpanel-error-no-body");

                MoveTo(visitor, thing, true);
                _adminLog.Add(LogType.Teleport, LogImpact.Low,
                    $"{session.Name} teleported to {ToPrettyString(thing)} from the God Panel");
                return true;

            case WFGodAction.Bring:
                if (session.AttachedEntity is not { } host || host == thing)
                    return Fail(out message, "wf-godpanel-error-no-body");

                if (HasComp<MapGridComponent>(thing) || HasComp<MapComponent>(thing))
                    return Fail(out message, "wf-godpanel-error-not-bringable");

                MoveTo(thing, host, false);
                _adminLog.Add(LogType.Teleport, LogImpact.Medium,
                    $"{session.Name} brought {ToPrettyString(thing)} to {ToPrettyString(host)} from the God Panel");
                return true;

            case WFGodAction.SecondChance:
                return _secondChance.TryOffer(thing, session, out message);

            case WFGodAction.OfferToGhosts:
                return _ghostOffer.TryOffer(thing, session, null, null, null, out message);

            case WFGodAction.Rejuvenate:
                _rejuvenate.PerformRejuvenate(thing);
                return true;

            case WFGodAction.PowerOn:
            case WFGodAction.PowerOff:
                if (!HasComp<MapGridComponent>(thing))
                    return Fail(out message, "wf-godpanel-error-not-grid");

                _gridPower.SetPower(action == WFGodAction.PowerOn, thing, flag);
                return true;

            case WFGodAction.Announce:
            {
                if (!HasComp<MapGridComponent>(thing))
                    return Fail(out message, "wf-godpanel-error-not-grid");

                var line = SharedChatSystem.SanitizeAnnouncement(text, MaxAnnouncementLength).Replace('\r', ' ')
                    .Replace('\n', ' ').Trim();
                if (line.Length == 0)
                    return Fail(out message, "wf-godpanel-error-no-text");

                if (!_pa.Announce(thing, line, sender: Loc.GetString("wf-godpanel-pa-sender", ("ship", _pa.GetShipName(thing)))))
                    return Fail(out message, "wf-godpanel-error-no-speakers");

                return true;
            }

            case WFGodAction.SetCode:
                if (!HasComp<MapGridComponent>(thing))
                    return Fail(out message, "wf-godpanel-error-not-grid");

                if (!_proto.HasIndex<ShipAlertCodePrototype>(text))
                    return Fail(out message, "wf-godpanel-error-bad-code");

                _alert.SetCode(thing, text);
                return true;

            case WFGodAction.GeneralQuartersOn:
            case WFGodAction.GeneralQuartersOff:
                if (!HasComp<MapGridComponent>(thing))
                    return Fail(out message, "wf-godpanel-error-not-grid");

                _alert.SetGeneralQuarters(thing, action == WFGodAction.GeneralQuartersOn);
                return true;

            case WFGodAction.DistressOn:
            case WFGodAction.DistressOff:
                if (!HasComp<MapGridComponent>(thing))
                    return Fail(out message, "wf-godpanel-error-not-grid");

                _alert.SetDistress(thing, action == WFGodAction.DistressOn);
                return true;

            case WFGodAction.HailAsGod:
            {
                if (!HasComp<MapGridComponent>(thing))
                    return Fail(out message, "wf-godpanel-error-not-grid");

                // The hail picks the powered consoles itself; it only needs one on the grid to start from.
                EntityUid? console = null;
                var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
                while (consoles.MoveNext(out var uid, out _, out var xform))
                {
                    if (xform.GridUid == thing)
                    {
                        console = uid;
                        break;
                    }
                }

                if (console == null || _adminHail.Start(session, console.Value) == null)
                    return Fail(out message, "wf-godpanel-error-no-console");

                return true;
            }

            case WFGodAction.PlayPaSound:
                if (!HasComp<MapGridComponent>(thing))
                    return Fail(out message, "wf-godpanel-error-not-grid");

                if (text.Length == 0)
                    return Fail(out message, "wf-godpanel-error-no-text");

                _internetSound.PlayOverPa(session, text, thing);
                return true;

            case WFGodAction.StopPaSound:
                if (!HasComp<MapGridComponent>(thing))
                    return Fail(out message, "wf-godpanel-error-not-grid");

                _internetSound.StopForGrid(thing);
                return true;

            case WFGodAction.EndEncounter:
            {
                var encounter = HasComp<WFEncounterComponent>(thing)
                    ? thing
                    : CompOrNull<WFEncounterGridComponent>(thing)?.Encounter;
                if (encounter is not { } found || !HasComp<WFEncounterComponent>(found))
                    return Fail(out message, "wf-godpanel-error-no-encounter");

                subject = found;
                _encounters.End(found);
                return true;
            }

            case WFGodAction.PlaySound:
                if (text.Length == 0)
                    return Fail(out message, "wf-godpanel-error-no-text");

                _internetSound.Play(session, text);
                return true;

            case WFGodAction.StopSound:
                _internetSound.Stop(session);
                return true;

            case WFGodAction.PauseEncounters:
            case WFGodAction.ResumeEncounters:
                _scheduler.Paused = action == WFGodAction.PauseEncounters;
                return true;

            case WFGodAction.ReaverPause:
            case WFGodAction.ReaverResume:
                _reavers.Paused = action == WFGodAction.ReaverPause;
                return true;

            case WFGodAction.WarLevelCold:
            case WFGodAction.WarLevelHot:
                _war.SetLevel(action == WFGodAction.WarLevelHot);
                return true;
        }

        return false;
    }

    private bool Fail(out string message, string key)
    {
        message = Loc.GetString(key);
        return false;
    }

    /// <summary>
    /// Puts <paramref name="mover"/> on <paramref name="destination"/>, the way the tpto command does; a grid is
    /// entered at its middle.
    /// </summary>
    private void MoveTo(EntityUid mover, EntityUid destination, bool stopFollowing)
    {
        if (stopFollowing && TryComp<FollowerComponent>(mover, out var follower))
            _follower.StopFollowingEntity(mover, follower.Following);

        var coordinates = TryComp<MapGridComponent>(destination, out var grid)
            ? new EntityCoordinates(destination, grid.LocalAABB.Center)
            : new EntityCoordinates(destination, System.Numerics.Vector2.Zero);

        if (grid == null && TryComp<PhysicsComponent>(destination, out var physics))
            coordinates = coordinates.Offset(physics.LocalCenter);

        var xform = Transform(mover);
        _transform.SetMapCoordinates((mover, xform), _transform.ToMapCoordinates(coordinates));
        _transform.AttachToGridOrMap(mover, xform);
        if (TryComp<PhysicsComponent>(mover, out var moverPhysics))
            _physics.SetLinearVelocity(mover, System.Numerics.Vector2.Zero, body: moverPhysics);
    }
}
