using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Body.Systems;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Commands;

/// <summary>
/// TEMPORARY (playtest 5): the heal behind <c>healmeimbroken</c>. Asks the player what broke, rejuvenates their body
/// and files an ahelp with the answer and the state they were in, which the ahelp relay carries to Discord. Delete
/// this file, <see cref="HealMeImBrokenCommand"/>, <c>BwoinkSystem.Wolfmed.cs</c>, the <c>healmeimbroken.ftl</c>
/// locale, the two <c>wolfmed.bug_rescue_*</c> cvars and <c>WolfmedBugRescueTest</c> once the playtest is over.
/// </summary>
public sealed class WolfmedBugRescueSystem : EntitySystem
{
    [Dependency] private BwoinkSystem _bwoink = default!;
    [Dependency] private HealthAnalyzerSystem _analyzer = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private QuickDialogSystem _dialog = default!;
    [Dependency] private RejuvenateSystem _rejuvenate = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private readonly Dictionary<NetUserId, TimeSpan> _lastUse = new();

    /// <summary>Opens the "what broke" window, or says why not.</summary>
    public bool TryBegin(ICommonSession session, [NotNullWhen(false)] out string? refusal)
    {
        if (!Allowed(session, out refusal))
            return false;

        // The dialog is one label beside one line edit: the explanation goes to chat, the window asks one question.
        _chat.DispatchServerMessage(session, Loc.GetString("healmeimbroken-brief"));
        _dialog.OpenDialog(session, Loc.GetString("healmeimbroken-title"), Loc.GetString("healmeimbroken-prompt"),
            (LongString reason) => Rescue(session, reason.String));
        return true;
    }

    /// <summary>Heals the session's body and files the ticket. False, with a chat line, when it is refused.</summary>
    public bool Rescue(ICommonSession session, string reason)
    {
        if (!Allowed(session, out var refusal))
        {
            _chat.DispatchServerMessage(session, refusal);
            return false;
        }

        reason = reason.Trim();
        if (reason.Length == 0)
        {
            _chat.DispatchServerMessage(session, Loc.GetString("healmeimbroken-empty"));
            return false;
        }

        var body = session.AttachedEntity!.Value;
        var report = BuildReport(session, body, reason);
        _lastUse[session.UserId] = _timing.CurTime;

        _rejuvenate.PerformRejuvenate(body);
        _adminLog.Add(LogType.AdminMessage, LogImpact.High,
            $"TEMPORARY healmeimbroken: {session.Name} rejuvenated {ToPrettyString(body):entity}. Reason: {reason}");
        _bwoink.SendPlayerBwoink(session, report);
        _chat.DispatchServerMessage(session, Loc.GetString("healmeimbroken-done"));
        return true;
    }

    /// <summary>The ticket: who, where and why, then the body's state and its wounds before the heal.</summary>
    public string BuildReport(ICommonSession session, EntityUid body, string reason)
    {
        var grid = Transform(body).GridUid is { } gridUid ? Name(gridUid) : Loc.GetString("healmeimbroken-space");
        var text = new StringBuilder();
        text.Append(Loc.GetString("healmeimbroken-report",
            ("player", session.Name), ("character", Name(body)), ("grid", grid), ("reason", reason)));

        if (_analyzer.BuildVitals(body) is { } vitals)
        {
            text.Append(' ').Append(Loc.GetString("healmeimbroken-report-state",
                ("state", WolfmedVitalsText.StateLine(vitals)), ("vitals", WolfmedVitalsText.VitalsLine(vitals))));
            if (WolfmedVitalsText.DoFirstLine(vitals) is { } advice)
                text.Append(' ').Append(Loc.GetString("healmeimbroken-report-dofirst", ("advice", advice)));
        }

        var parts = new List<string>();
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            var counts = new Dictionary<string, int>();
            foreach (var wound in _wounds.GetWounds(part))
            {
                if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                    !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype))
                    continue;

                var name = Loc.GetString(prototype.Name);
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }

            if (counts.Count == 0)
                continue;

            var names = counts.Select(entry => entry.Value > 1
                ? Loc.GetString("healmeimbroken-wound-count", ("count", entry.Value), ("name", entry.Key))
                : entry.Key);
            parts.Add(Loc.GetString("healmeimbroken-part-wounds", ("part", Name(part)), ("wounds", string.Join(", ", names))));
        }

        text.Append(' ').Append(parts.Count == 0
            ? Loc.GetString("healmeimbroken-report-no-wounds")
            : Loc.GetString("healmeimbroken-report-wounds", ("wounds", string.Join("; ", parts))));
        return text.ToString();
    }

    private bool Allowed(ICommonSession session, [NotNullWhen(false)] out string? refusal)
    {
        refusal = null;
        if (!_cfg.GetCVar(WolfmedCVars.BugRescueEnabled))
            refusal = Loc.GetString("healmeimbroken-disabled");
        else if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { Valid: true } body ||
                 !HasComp<MobStateComponent>(body) || HasComp<GhostComponent>(body))
            refusal = Loc.GetString("healmeimbroken-not-in-game");
        else if (_lastUse.TryGetValue(session.UserId, out var last))
        {
            var since = (_timing.CurTime - last).TotalSeconds;
            var cooldown = _cfg.GetCVar(WolfmedCVars.BugRescueCooldown);
            if (since < cooldown)
                refusal = Loc.GetString("healmeimbroken-cooldown", ("seconds", (int) since), ("wait", (int) (cooldown - since)));
        }

        return refusal == null;
    }
}
