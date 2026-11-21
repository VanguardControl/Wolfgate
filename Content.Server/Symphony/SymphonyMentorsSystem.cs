using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Server._WG.Mentors;
using Content.Shared._WG.Mentors;
using Content.Shared.Symphony;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Server.Symphony;

/// <summary>
/// The mentor roster for the SSymphony panel. The game owns mentor status; the panel reads the roster here (or straight
/// from wolfgate_mentor), changes it through the same rules the game uses, reviews applications, and reads the
/// leaderboard. It gives the one mentor Discord role to linked mentors whose row is active (D13). Behind the admin
/// API token.
/// </summary>
public sealed class SymphonyMentorsSystem : EntitySystem
{
    /// <summary>GET: the roster. POST: grant, revoke, change tier or restore.</summary>
    public const string RosterPath = "/symphony/mentors";

    /// <summary>POST: re-read rows the panel wrote itself, for connected players.</summary>
    public const string RefreshPath = "/symphony/mentors/refresh";

    /// <summary>POST: approve or deny a pending application.</summary>
    public const string ApplicationPath = "/symphony/mentors/application";

    /// <summary>GET with ?period=week|month|all: the ranked leaderboard.</summary>
    public const string LeaderboardPath = "/symphony/mentors/leaderboard";

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WGMentorSystem _mentors = default!;
    [Dependency] private WGMentorApplicationSystem _applications = default!;
    [Dependency] private WGMentorBoardSystem _board = default!;

    public override void Initialize()
    {
        base.Initialize();
        _statusHost.AddHandler(HandleAsync);
    }

    // Runs on the status host's thread; the work itself hops to the main thread.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        var path = context.Url.AbsolutePath;
        if (path != RosterPath && path != RefreshPath && path != ApplicationPath && path != LeaderboardPath)
            return false;
        if (!await SymphonyApi.AuthorisedAsync(_cfg, context))
            return true;

        var get = context.RequestMethod == HttpMethod.Get;
        var post = context.RequestMethod == HttpMethod.Post;
        switch (path)
        {
            case RosterPath when get:
                await RespondRoster(context);
                break;
            case RosterPath when post:
                await RespondChange(context);
                break;
            case RefreshPath when post:
                await RespondRefresh(context);
                break;
            case ApplicationPath when post:
                await RespondApplication(context);
                break;
            case LeaderboardPath when get:
                await RespondLeaderboard(context);
                break;
            default:
                await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
                break;
        }

        return true;
    }

    private async Task RespondRoster(IStatusHandlerContext context)
    {
        var rows = await await _tasks.OnMainThread(() => _mentors.ListAsync());
        await context.RespondJsonAsync(new RosterResponse
        {
            SymphonyModule = SharedSymphony.ModuleVersion,
            Tiers = Enum.GetValues<MentorTier>().Select(MentorRows.TierToDb).ToList(),
            Mentors = rows.Select(r => new RosterEntry
            {
                UserId = r.UserId,
                Tier = r.Tier,
                State = r.State,
                Active = r.State == MentorRows.StateActive,
                Source = r.Source,
                GrantedAt = r.GrantedAt,
                LastActiveAt = r.LastActiveAt,
                PromotedAt = r.PromotedAt,
            }).ToList(),
        });
    }

    private async Task RespondChange(IStatusHandlerContext context)
    {
        var body = await ReadAsync<ChangeBody>(context);
        var tier = MentorTier.Trainee;
        if (body?.UserId is not { } raw
            || !Guid.TryParse(raw, out var id)
            || body.Action is not ("grant" or "revoke" or "tier" or "restore")
            || (body.Action is "grant" or "tier" && !MentorRows.TryParseTier(body.Tier ?? "trainee", out tier))
            || (body.Action == "revoke" && string.IsNullOrWhiteSpace(body.Reason)))
        {
            await context.RespondAsync(
                "expected {\"user_id\": \"<uuid>\", \"action\": \"grant|revoke|tier|restore\", \"tier\": \"trainee|mentor|senior\", \"reason\": \"needed to revoke\", \"actor\": \"optional\"}",
                HttpStatusCode.BadRequest);
            return;
        }

        var user = new NetUserId(id);
        var done = body.Action switch
        {
            "grant" => await await _tasks.OnMainThread(async () =>
            {
                await _mentors.GrantAsync(user, tier, null, MentorRows.SourcePanel);
                return true;
            }),
            "revoke" => await await _tasks.OnMainThread(() => _mentors.RevokeAsync(user, null, Actor(body.Actor, body.Reason!))),
            "tier" => await await _tasks.OnMainThread(() => _mentors.SetTierAsync(user, tier, null)),
            _ => await await _tasks.OnMainThread(async () => await _mentors.RestoreAsync(user, null) != null),
        };

        if (!done)
        {
            await context.RespondErrorAsync(HttpStatusCode.Conflict);
            return;
        }

        Log.Info($"{body.Actor ?? "The panel"} made a mentor change: {body.Action} {id}");
        await context.RespondJsonAsync(new ChangeResponse { SymphonyModule = SharedSymphony.ModuleVersion, Done = true });
    }

    private async Task RespondRefresh(IStatusHandlerContext context)
    {
        using var reader = new StreamReader(context.RequestBody);
        var text = await reader.ReadToEndAsync();
        List<Guid>? users = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            RefreshBody? body;
            try
            {
                body = JsonSerializer.Deserialize<RefreshBody>(text);
            }
            catch (JsonException)
            {
                await context.RespondAsync("expected {\"users\": [\"<uuid>\", ...]} or no body", HttpStatusCode.BadRequest);
                return;
            }

            if (body?.Users is { } raw)
            {
                users = new List<Guid>();
                foreach (var entry in raw)
                {
                    if (!Guid.TryParse(entry, out var user))
                    {
                        await context.RespondAsync($"not a user id: {entry}", HttpStatusCode.BadRequest);
                        return;
                    }

                    users.Add(user);
                }
            }
        }

        var refreshed = await await _tasks.OnMainThread(() => _mentors.RefreshAsync(users));
        await context.RespondJsonAsync(new RefreshResponse { SymphonyModule = SharedSymphony.ModuleVersion, Online = refreshed });
    }

    private async Task RespondApplication(IStatusHandlerContext context)
    {
        var body = await ReadAsync<ApplicationBody>(context);
        if (body?.Id is not { } id || body.Decision is not ("approve" or "deny"))
        {
            await context.RespondAsync(
                "expected {\"id\": 1, \"decision\": \"approve|deny\", \"reason\": \"needed to deny, shown to the player\", \"reapply_days\": 30, \"actor\": \"optional\"}",
                HttpStatusCode.BadRequest);
            return;
        }

        var approve = body.Decision == "approve";
        var problem = await await _tasks.OnMainThread(() =>
            _applications.ReviewAsync(id, approve, body.Reason ?? "", body.ReapplyDays ?? 0, null));

        if (problem != null)
        {
            await context.RespondAsync(problem, HttpStatusCode.Conflict);
            return;
        }

        Log.Info($"{body.Actor ?? "The panel"} {(approve ? "approved" : "denied")} mentor application #{id}");
        await context.RespondJsonAsync(new ChangeResponse { SymphonyModule = SharedSymphony.ModuleVersion, Done = true });
    }

    private async Task RespondLeaderboard(IStatusHandlerContext context)
    {
        var query = context.Url.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .DistinctBy(p => p[0])
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

        var period = query.GetValueOrDefault("period", "all") switch
        {
            "week" => MentorBoardPeriod.Week,
            "month" => MentorBoardPeriod.Month,
            "all" => MentorBoardPeriod.All,
            _ => (MentorBoardPeriod?) null,
        };

        if (period == null)
        {
            await context.RespondAsync("expected ?period=week|month|all", HttpStatusCode.BadRequest);
            return;
        }

        var board = await await _tasks.OnMainThread(() => _board.GetAsync(period.Value));
        await context.RespondJsonAsync(new LeaderboardResponse
        {
            SymphonyModule = SharedSymphony.ModuleVersion,
            Period = query.GetValueOrDefault("period", "all"),
            Fallbacks = board.Fallbacks,
            Mentors = board.Rows.Select((r, i) => new LeaderboardEntry
            {
                Rank = i + 1,
                UserId = r.User,
                Name = r.Row.Name,
                Resolved = r.Row.Resolved,
                Helpful = r.Row.Helpful,
                Rated = r.Row.Rated,
                MedianFirstAnswerSeconds = r.Row.MedianFirstAnswer,
                Assists = r.Row.Assists,
                Escalations = r.Row.Escalations,
            }).ToList(),
        });
    }

    private static string Actor(string? actor, string reason)
    {
        return string.IsNullOrWhiteSpace(actor) ? reason : $"{reason} ({actor})";
    }

    private static async Task<T?> ReadAsync<T>(IStatusHandlerContext context) where T : class
    {
        try
        {
            return await context.RequestBodyJsonAsync<T>();
        }
        catch (Exception)
        {
            // Not JSON: refused by the caller.
            return null;
        }
    }

    private sealed class RosterResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("tiers")] public List<string> Tiers { get; set; } = new();
        [JsonPropertyName("mentors")] public List<RosterEntry> Mentors { get; set; } = new();
    }

    private sealed class RosterEntry
    {
        [JsonPropertyName("user_id")] public Guid UserId { get; set; }
        [JsonPropertyName("tier")] public string Tier { get; set; } = "";
        [JsonPropertyName("state")] public string State { get; set; } = "";
        [JsonPropertyName("active")] public bool Active { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("granted_at")] public DateTime GrantedAt { get; set; }
        [JsonPropertyName("last_active_at")] public DateTime LastActiveAt { get; set; }
        [JsonPropertyName("promoted_at")] public DateTime? PromotedAt { get; set; }
    }

    private sealed class ChangeBody
    {
        [JsonPropertyName("user_id")] public string? UserId { get; set; }
        [JsonPropertyName("action")] public string? Action { get; set; }
        [JsonPropertyName("tier")] public string? Tier { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("actor")] public string? Actor { get; set; }
    }

    private sealed class ChangeResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("done")] public bool Done { get; set; }
    }

    private sealed class RefreshBody
    {
        [JsonPropertyName("users")] public List<string>? Users { get; set; }
    }

    private sealed class RefreshResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("online")] public int Online { get; set; }
    }

    private sealed class ApplicationBody
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("decision")] public string? Decision { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("reapply_days")] public int? ReapplyDays { get; set; }
        [JsonPropertyName("actor")] public string? Actor { get; set; }
    }

    private sealed class LeaderboardResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("period")] public string Period { get; set; } = "";
        [JsonPropertyName("fallbacks")] public int Fallbacks { get; set; }
        [JsonPropertyName("mentors")] public List<LeaderboardEntry> Mentors { get; set; } = new();
    }

    private sealed class LeaderboardEntry
    {
        [JsonPropertyName("rank")] public int Rank { get; set; }
        [JsonPropertyName("user_id")] public Guid UserId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("resolved")] public int Resolved { get; set; }
        [JsonPropertyName("helpful")] public int Helpful { get; set; }
        [JsonPropertyName("rated")] public int Rated { get; set; }
        [JsonPropertyName("median_first_answer_seconds")] public float? MedianFirstAnswerSeconds { get; set; }
        [JsonPropertyName("assists")] public int Assists { get; set; }
        [JsonPropertyName("escalations")] public int Escalations { get; set; }
    }
}
