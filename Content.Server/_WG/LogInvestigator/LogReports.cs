using System.Linq;
using Content.Server.Database;
using Content.Shared._WG.LogInvestigator;
using Content.Shared.Database;

namespace Content.Server._WG.LogInvestigator;

/// <summary>
/// The investigator's scripted reports, built from rows the database already returned: who hit whom in an encounter,
/// one player's round in phases, and who was near a place. Nothing here guesses intent; it lines up what the logs say.
/// </summary>
public static class LogReports
{
    /// <summary>Log types that record one entity hitting another.</summary>
    public static readonly HashSet<LogType> CombatTypes = new()
    {
        LogType.MeleeHit, LogType.BulletHit, LogType.HitScanHit, LogType.ThrowHit, LogType.ExplosionHit,
    };

    private static readonly string[] AttackerKeys = { "actor", "user", "source", "thrower", "player" };
    private static readonly string[] VictimKeys = { "subject", "target", "victim" };
    private static readonly string[] WeaponKeys = { "tool", "weapon", "projectile", "used", "entity", "thrown" };

    /// <summary>How far apart a turret's hit and its ship's "fired weaponry" log may be to credit the gunner.</summary>
    public static readonly TimeSpan GunnerWindow = TimeSpan.FromSeconds(5);

    /// <summary>How long before the first hit the encounter report looks for chat and hails.</summary>
    public static readonly TimeSpan ChatLookBack = TimeSpan.FromMinutes(10);

    /// <summary>Gap that splits one phase of a player's timeline from the next.</summary>
    public static readonly TimeSpan PhaseGap = TimeSpan.FromSeconds(60);

    private const int MaxLines = 200;

    /// <summary>The most logs a timeline phase shows; a longer one shows its first and last halves.</summary>
    private const int PhaseRows = 20;

    /// <summary>One entity hitting another, with turret hits credited to whoever fired the ship's guns.</summary>
    public sealed record Hit(DateTime At, string Attacker, Guid? AttackerPlayer, int AttackerId, string Victim, Guid? VictimPlayer, int VictimId, string Weapon, float Damage, LogRef VictimRef);

    /// <summary>The hits in combat logs; a hit by something no player controls is credited to a gunner of its ship nearby in time.</summary>
    public static List<Hit> Hits(IEnumerable<LogRow> combat, IReadOnlyList<LogRow> shipguns)
    {
        var hits = new List<Hit>();
        foreach (var row in combat)
        {
            var attacker = First(row, AttackerKeys);
            var victim = First(row, VictimKeys);
            if (attacker == null || victim == null || attacker.Id == victim.Id)
                continue;

            var weapon = First(row, WeaponKeys, exclude: attacker.Id, excludeToo: victim.Id);
            var label = Label(attacker);
            var player = attacker.Player;

            if (player == null && attacker.Grid is { } grid && Gunner(shipguns, grid, row.Date) is { } gunner)
            {
                label = Loc.GetString("wg-log-report-via", ("gunner", Label(gunner)), ("gun", attacker.Name ?? attacker.Id.ToString()));
                player = gunner.Player;
            }

            hits.Add(new Hit(row.Date, label, player, attacker.Id, Label(victim), victim.Player, victim.Id,
                weapon?.Name ?? attacker.Name ?? "?", row.Damage ?? 0, victim));
        }

        return hits;
    }

    /// <summary>The player who fired this ship's guns closest in time to the hit, if within the window.</summary>
    private static LogRef? Gunner(IReadOnlyList<LogRow> shipguns, int grid, DateTime at)
    {
        LogRef? best = null;
        var bestGap = GunnerWindow;
        foreach (var fired in shipguns)
        {
            var gap = (fired.Date - at).Duration();
            if (gap > bestGap || !fired.Refs.Any(r => r.Key == "entity" && r.Id == grid))
                continue;

            if (fired.Refs.FirstOrDefault(r => r.Key == "user" && r.Player != null) is not { } user)
                continue;

            best = user;
            bestGap = gap;
        }

        return best;
    }

    /// <summary>Who hit whom among player-involved hits, who struck first, deaths, and what the parties said before it.</summary>
    public static LogReport Encounter(List<Hit> hits, IEnumerable<LogRow> deaths, IEnumerable<LogRow> chatBefore, DateTime? roundStart, List<LogPlayerNearby>? nearby)
    {
        var sections = new List<LogReportSection>();
        var pvp = hits.Where(h => h.VictimPlayer != null && (h.AttackerPlayer != null || h.Attacker != h.Victim)).OrderBy(h => h.At).ToList();
        var npc = hits.Where(h => h.VictimPlayer == null && h.AttackerPlayer != null).ToList();

        if (pvp.Count == 0)
        {
            sections.Add(new LogReportSection(Loc.GetString("wg-log-report-hits"), new List<string> { Loc.GetString("wg-log-report-no-hits") }));
        }
        else
        {
            var first = pvp[0];
            sections.Add(new LogReportSection(Loc.GetString("wg-log-report-first-strike"), new List<string>
            {
                Loc.GetString("wg-log-report-first-strike-line",
                    ("time", Time(first.At, roundStart)),
                    ("attacker", first.Attacker),
                    ("victim", first.Victim),
                    ("weapon", first.Weapon)),
            }));

            // Grouped by who, not by name: two characters can share a name, and gunners of one ship stay apart.
            var table = pvp
                .GroupBy(h => (Attacker: Identity(h.AttackerPlayer, h.AttackerId), Victim: Identity(h.VictimPlayer, h.VictimId)))
                .OrderBy(g => g.Min(h => h.At))
                .Select(g => Loc.GetString("wg-log-report-hit-line",
                    ("attacker", g.First().Attacker),
                    ("victim", g.First().Victim),
                    ("hits", g.Count()),
                    ("damage", (int) g.Sum(h => h.Damage)),
                    ("weapons", string.Join(", ", g.Select(h => h.Weapon).Distinct().Take(4))),
                    ("first", Time(g.Min(h => h.At), roundStart)),
                    ("last", Time(g.Max(h => h.At), roundStart))))
                .Take(MaxLines)
                .ToList();
            sections.Add(new LogReportSection(Loc.GetString("wg-log-report-hits"), table));
        }

        if (npc.Count > 0)
        {
            var lines = npc
                .GroupBy(h => Identity(h.AttackerPlayer, h.AttackerId))
                .OrderByDescending(g => g.Count())
                .Select(g => Loc.GetString("wg-log-report-npc-line", ("attacker", g.First().Attacker), ("hits", g.Count()), ("targets", g.Select(h => h.VictimId).Distinct().Count())))
                .Take(20)
                .ToList();
            sections.Add(new LogReportSection(Loc.GetString("wg-log-report-npc"), lines));
        }

        var deathRows = deaths
            .Where(d => d.Message.Contains(" to Dead", StringComparison.Ordinal))
            .OrderBy(d => d.Date)
            .Take(MaxLines)
            .ToList();
        if (deathRows.Count > 0)
            sections.Add(new LogReportSection(Loc.GetString("wg-log-report-deaths"), new List<string>(), deathRows));

        var chatRows = chatBefore
            .OrderBy(c => c.Date)
            .TakeLast(MaxLines)
            .ToList();
        sections.Add(new LogReportSection(Loc.GetString("wg-log-report-chat-before"),
            chatRows.Count > 0 ? new List<string>() : new List<string> { Loc.GetString("wg-log-report-no-chat") },
            chatRows));

        if (nearby != null)
            sections.Add(NearbySection(nearby, roundStart));

        return new LogReport(LogReportKind.Encounter, Loc.GetString("wg-log-report-encounter-title"), sections);
    }

    /// <summary>
    /// A player's logs run together into phases by category, split where they go quiet for a minute. Each phase is a
    /// section holding its logs, shown like the result list; a long phase keeps its first and last logs.
    /// </summary>
    public static LogReport Timeline(string player, IEnumerable<LogRow> rows, IReadOnlyDictionary<LogType, string> categories, DateTime? roundStart)
    {
        var phases = new List<(string Category, List<LogRow> Rows)>();
        foreach (var row in rows.OrderBy(r => r.Date).ThenBy(r => r.Id))
        {
            var category = categories.GetValueOrDefault(row.Type, row.Type.ToString());
            if (phases.Count > 0 && phases[^1].Category == category && row.Date - phases[^1].Rows[^1].Date <= PhaseGap)
            {
                phases[^1].Rows.Add(row);
                continue;
            }

            phases.Add((category, new List<LogRow> { row }));
        }

        var sections = new List<LogReportSection>();
        var shown = 0;
        foreach (var (category, phase) in phases)
        {
            if (shown >= MaxLines * 2)
            {
                sections.Add(new LogReportSection(Loc.GetString("wg-log-report-timeline-cut", ("phases", phases.Count - sections.Count)), new List<string>()));
                break;
            }

            var title = Loc.GetString("wg-log-report-phase-title",
                ("from", Time(phase[0].Date, roundStart)),
                ("to", Time(phase[^1].Date, roundStart)),
                ("category", category),
                ("count", phase.Count));

            var kept = phase.Count <= PhaseRows ? phase : phase.Take(PhaseRows / 2).Concat(phase.TakeLast(PhaseRows / 2)).ToList();
            var lines = phase.Count > PhaseRows
                ? new List<string> { Loc.GetString("wg-log-report-phase-skipped", ("count", phase.Count - kept.Count)) }
                : new List<string>();
            sections.Add(new LogReportSection(title, lines, kept));
            shown += kept.Count;
        }

        if (sections.Count == 0)
            sections.Add(new LogReportSection(Loc.GetString("wg-log-report-phases"), new List<string> { Loc.GetString("wg-log-report-no-logs") }));

        return new LogReport(LogReportKind.Timeline, Loc.GetString("wg-log-report-timeline-title", ("player", player)), sections);
    }

    /// <summary>A player's closest approach to a point in the window.</summary>
    public sealed record LogPlayerNearby(Guid Player, string Name, float Distance, DateTime At, bool SameGrid, bool Alive, bool Spoke);

    /// <summary>Players who came within the radius of the point, or were on its grid, closest first.</summary>
    public static List<LogPlayerNearby> Nearby(LogPoint point, int radius, IEnumerable<WolfgatePlayerPosition> positions,
        IReadOnlyDictionary<Guid, string> names, ISet<Guid> spoke)
    {
        var closest = new Dictionary<Guid, LogPlayerNearby>();
        foreach (var sample in positions)
        {
            if (sample.MapId != point.Map)
                continue;

            var sameGrid = point.Grid != null && sample.GridId == point.Grid;
            var distance = MathF.Sqrt((sample.X - point.X) * (sample.X - point.X) + (sample.Y - point.Y) * (sample.Y - point.Y));
            if (distance > radius && !sameGrid)
                continue;

            if (closest.TryGetValue(sample.PlayerUserId, out var known) && known.Distance <= distance)
            {
                if (sameGrid && !known.SameGrid)
                    closest[sample.PlayerUserId] = known with { SameGrid = true };
                continue;
            }

            var name = names.TryGetValue(sample.PlayerUserId, out var username)
                ? $"{sample.EntityName} ({username})"
                : sample.EntityName ?? sample.PlayerUserId.ToString();
            closest[sample.PlayerUserId] = new LogPlayerNearby(sample.PlayerUserId, name, distance, sample.SampledAt,
                sameGrid || (known?.SameGrid ?? false), sample.Alive, spoke.Contains(sample.PlayerUserId));
        }

        return closest.Values.OrderBy(n => n.Distance).ToList();
    }

    public static LogReport NearbyReport(LogPoint point, int radius, List<LogPlayerNearby> nearby, DateTime? roundStart)
    {
        return new LogReport(LogReportKind.Nearby,
            Loc.GetString("wg-log-report-nearby-title", ("radius", radius), ("time", Time(point.At, roundStart))),
            new List<LogReportSection> { NearbySection(nearby, roundStart) });
    }

    private static LogReportSection NearbySection(List<LogPlayerNearby> nearby, DateTime? roundStart)
    {
        var lines = nearby
            .Select(n => Loc.GetString("wg-log-report-nearby-line",
                ("name", n.Name),
                ("distance", MathF.Round(n.Distance, 1)),
                ("time", Time(n.At, roundStart)),
                ("sameGrid", n.SameGrid),
                ("alive", n.Alive),
                ("spoke", n.Spoke)))
            .Take(MaxLines)
            .ToList();
        if (lines.Count == 0)
            lines.Add(Loc.GetString("wg-log-report-nobody-nearby"));

        return new LogReportSection(Loc.GetString("wg-log-report-nearby"), lines);
    }

    /// <summary>Round time when the round's start is known, otherwise UTC clock time.</summary>
    public static string Time(DateTime at, DateTime? roundStart)
    {
        if (roundStart is not { } start)
            return at.ToString("HH:mm:ss");

        var since = at - start;
        return since < TimeSpan.Zero ? at.ToString("HH:mm:ss") : $"{(int) since.TotalHours}:{since:mm\\:ss}";
    }

    private static LogRef? First(LogRow row, string[] keys, int? exclude = null, int? excludeToo = null)
    {
        foreach (var key in keys)
        {
            foreach (var reference in row.Refs)
            {
                if (reference.Key == key && reference.Id != exclude && reference.Id != excludeToo)
                    return reference;
            }
        }

        return null;
    }

    /// <summary>A participant: their player when they have one, otherwise the entity.</summary>
    private static string Identity(Guid? player, int entity)
    {
        return player is { } id ? id.ToString() : $"e{entity}";
    }

    private static string Label(LogRef reference)
    {
        return reference.Name ?? reference.Id.ToString();
    }
}
