using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server._WF.AppearanceStudio;
using Content.Server._WF.CustomMarkings;
using Content.Server._WF.FeatureBans;
using Content.Shared._WF.AppearanceStudio;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Symphony;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Server.Symphony;

/// <summary>
/// The web creator's custom marking library and studio warps: list, save and delete library entries, and store a
/// warp. Saves go through the same code as the game's editor (CustomMarkingSystem, StudioSystem): the art arrives as
/// raw RGBA, never an image file, and the game checks, hashes and encodes it. Behind the admin API token; contract in
/// the README.
/// </summary>
public sealed class SymphonyCustomMarkingsSystem : EntitySystem
{
    public const string LibraryPath = "/symphony/custom-markings";
    public const string WarpsPath = "/symphony/studio/warps";

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private CustomMarkingSystem _customMarkings = default!;
    [Dependency] private StudioSystem _studio = default!;
    [Dependency] private FeatureBanSystem _featureBans = default!;

    public override void Initialize()
    {
        base.Initialize();
        _statusHost.AddHandler(HandleAsync);
    }

    // Runs on the status host's thread; the work itself hops to the main thread.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        var path = context.Url.AbsolutePath;
        if (path != LibraryPath && path != WarpsPath)
            return false;

        if (!await SymphonyApi.AuthorisedAsync(_cfg, context))
            return true;

        var query = System.Web.HttpUtility.ParseQueryString(context.Url.Query);
        if (!Guid.TryParse(query["user"], out var guid))
        {
            await context.RespondJsonAsync(SymphonyJson.Error("bad_request", "user=<guid> is required"), HttpStatusCode.BadRequest);
            return true;
        }

        var user = new NetUserId(guid);
        var method = context.RequestMethod;
        SymphonyCharactersSystem.Answer answer;
        try
        {
            if (path == LibraryPath && method == HttpMethod.Get)
            {
                answer = await await _tasks.OnMainThread(() => List(user));
            }
            else if (path == LibraryPath && method == HttpMethod.Post)
            {
                var body = await ReadBody(context);
                answer = await await _tasks.OnMainThread(() => Save(user, body));
            }
            else if (path == LibraryPath && method == HttpMethod.Delete)
            {
                if (!int.TryParse(query["id"], out var id))
                {
                    await context.RespondJsonAsync(SymphonyJson.Error("bad_request", "id=<int> is required"), HttpStatusCode.BadRequest);
                    return true;
                }

                answer = await await _tasks.OnMainThread(() => Delete(user, id));
            }
            else if (path == WarpsPath && method == HttpMethod.Post)
            {
                var body = await ReadBody(context);
                answer = await await _tasks.OnMainThread(() => SaveWarp(user, body));
            }
            else
            {
                await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
                return true;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Symphony custom marking request {method} {path} for {user} threw: {e}");
            answer = new SymphonyCharactersSystem.Answer(HttpStatusCode.InternalServerError, SymphonyJson.Error("failed"));
        }

        if (answer.Code == HttpStatusCode.NoContent)
            await context.RespondNoContentAsync();
        else
            await context.RespondJsonAsync(answer.Body, answer.Code);

        return true;
    }

    private static async Task<JsonObject?> ReadBody(IStatusHandlerContext context)
    {
        try
        {
            return await JsonNode.ParseAsync(context.RequestBody) as JsonObject;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>GET /symphony/custom-markings. Main thread.</summary>
    public async Task<SymphonyCharactersSystem.Answer> List(NetUserId user)
    {
        var rows = await _db.GetCustomMarkingsAsync(user);
        var entries = new JsonArray();
        foreach (var row in rows)
        {
            entries.Add(Entry(row.Id, row.Name, row.ArtHash, Placement(row.Placement)));
        }

        var body = new JsonObject
        {
            ["symphony_module"] = SharedSymphony.ModuleVersion,
            ["entries"] = entries,
            ["library_limit"] = _cfg.GetCVar(CustomMarkingCVars.LibraryLimit),
            ["daily_art_limit"] = _cfg.GetCVar(CustomMarkingCVars.DailyArtLimit),
            ["banned"] = await _featureBans.IsBannedAsync(user, StudioRules.Feature),
        };

        await AddDailyLeft(user, body);
        return new SymphonyCharactersSystem.Answer(HttpStatusCode.OK, body);
    }

    /// <summary>POST /symphony/custom-markings. Main thread.</summary>
    public async Task<SymphonyCharactersSystem.Answer> Save(NetUserId user, JsonObject? body)
    {
        if (body == null
            || body["placement"]?.GetValue<string>() is not { } placementName
            || !Enum.TryParse<CustomMarkingPlacement>(placementName, out var placement)
            || !Enum.IsDefined(placement))
            return Error(HttpStatusCode.BadRequest, "bad_request", "expected {\"id\", \"name\", \"placement\", \"art\"}");

        if (await _db.GetServerBanAsync(null, user, null, null) != null)
            return Error(HttpStatusCode.Forbidden, "banned");

        var id = body["id"]?.GetValue<int>() ?? 0;
        var name = body["name"]?.GetValue<string>() ?? string.Empty;

        byte[]? pixels = null;
        int[]? times = null;
        byte[]? erase = null;
        var pingPong = false;
        if (body["art"] is JsonObject art)
        {
            if (!TryBase64(art["pixels"], out pixels) || pixels == null)
                return Error(HttpStatusCode.BadRequest, "invalid", "art.pixels must be base64 RGBA");

            if (art["frameTimes"] is JsonArray frameTimes)
                times = frameTimes.Select(t => t?.GetValue<int>() ?? CustomMarkingRules.DefaultFrameTime).ToArray();

            if (!TryBase64(art["erase"], out erase))
                return Error(HttpStatusCode.BadRequest, "invalid", "art.erase must be base64 or null");

            pingPong = art["pingPong"]?.GetValue<bool>() ?? false;
        }

        var msg = new CustomMarkingSaveEvent(0, id, name, placement, pixels, times, erase, pingPong);
        var (result, eraseDropped) = await _customMarkings.Save(user, "SSymphony", msg);
        if (result.Error != null || result.Entry is not { } row)
        {
            var failed = MarkingError(result.Error, user);
            if (failed.Body is JsonObject failedBody)
                await AddDailyLeft(user, failedBody);

            return failed;
        }

        var answer = new JsonObject
        {
            ["entry"] = Entry(row.Id, row.Name, row.ArtHash, Placement(row.Placement)),
            ["previous_hash"] = result.PreviousHash,
            ["erase_dropped"] = eraseDropped,
        };

        await AddDailyLeft(user, answer);
        return new SymphonyCharactersSystem.Answer(HttpStatusCode.OK, answer);
    }

    /// <summary>DELETE /symphony/custom-markings. Main thread.</summary>
    public async Task<SymphonyCharactersSystem.Answer> Delete(NetUserId user, int id)
    {
        return await _db.DeleteCustomMarkingAsync(user, id)
            ? new SymphonyCharactersSystem.Answer(HttpStatusCode.NoContent, new JsonObject())
            : Error(HttpStatusCode.NotFound, "missing");
    }

    /// <summary>POST /symphony/studio/warps. Main thread.</summary>
    public async Task<SymphonyCharactersSystem.Answer> SaveWarp(NetUserId user, JsonObject? body)
    {
        if (body == null || !TryBase64(body["pixels"], out var pixels) || pixels == null)
            return Error(HttpStatusCode.BadRequest, "bad_request", "expected {\"pixels\": \"<base64 RGBA, one frame>\"}");

        if (await _db.GetServerBanAsync(null, user, null, null) != null)
            return Error(HttpStatusCode.Forbidden, "banned");

        string? hash;
        string? error;
        try
        {
            (hash, error) = await _studio.SaveWarp(user, "SSymphony", pixels);
        }
        catch (Exception e)
        {
            Log.Error($"Saving a studio warp from the web for {user} threw: {e}");
            return Error(HttpStatusCode.InternalServerError, "failed");
        }

        JsonObject answerBody;
        HttpStatusCode code;
        if (hash != null)
        {
            answerBody = new JsonObject { ["hash"] = hash };
            code = HttpStatusCode.OK;
        }
        else
        {
            var failed = WarpError(error, user);
            answerBody = (JsonObject) failed.Body;
            code = failed.Code;
        }

        await AddDailyLeft(user, answerBody);
        return new SymphonyCharactersSystem.Answer(code, answerBody);
    }

    /// <summary>Adds "daily_art_left" (null when there is no daily limit).</summary>
    private async Task AddDailyLeft(NetUserId user, JsonObject body)
    {
        var limit = _cfg.GetCVar(CustomMarkingCVars.DailyArtLimit);
        if (limit <= 0)
        {
            body["daily_art_left"] = null;
            return;
        }

        var used = await _db.CountCustomMarkingArtSinceAsync(user, DateTime.UtcNow - TimeSpan.FromDays(1));
        body["daily_art_left"] = Math.Max(0, limit - used);
    }

    private SymphonyCharactersSystem.Answer MarkingError(string? error, NetUserId user)
    {
        return error switch
        {
            "wf-custom-marking-error-disabled" => Error(HttpStatusCode.ServiceUnavailable, "disabled"),
            "wf-feature-banned" => Error(HttpStatusCode.Forbidden, "feature_banned"),
            "wf-custom-marking-error-cooldown" => Cooldown(_customMarkings.CooldownLeft(user)),
            "wf-custom-marking-error-blank" => Error(HttpStatusCode.BadRequest, "blank"),
            "wf-custom-marking-error-missing" => Error(HttpStatusCode.NotFound, "missing"),
            "wf-custom-marking-error-full" => Full(),
            "wf-custom-marking-error-blocked" => Error(HttpStatusCode.Conflict, "blocked"),
            "wf-custom-marking-error-daily" => Error(HttpStatusCode.TooManyRequests, "daily"),
            "wf-custom-marking-error-invalid" => Error(HttpStatusCode.BadRequest, "invalid"),
            _ => Error(HttpStatusCode.InternalServerError, "failed", error),
        };
    }

    private SymphonyCharactersSystem.Answer WarpError(string? error, NetUserId user)
    {
        return error switch
        {
            "wf-studio-error-disabled" => Error(HttpStatusCode.ServiceUnavailable, "disabled"),
            "wf-feature-banned" => Error(HttpStatusCode.Forbidden, "feature_banned"),
            "wf-studio-error-cooldown" => Cooldown(_studio.CooldownLeft(user)),
            "wf-studio-error-invalid" => Error(HttpStatusCode.BadRequest, "invalid"),
            "wf-studio-error-neutral" => Error(HttpStatusCode.BadRequest, "neutral"),
            "wf-studio-error-blocked" => Error(HttpStatusCode.Conflict, "blocked"),
            "wf-studio-error-daily" => Error(HttpStatusCode.TooManyRequests, "daily"),
            _ => Error(HttpStatusCode.InternalServerError, "failed", error),
        };
    }

    private SymphonyCharactersSystem.Answer Full()
    {
        var answer = Error(HttpStatusCode.Conflict, "full");
        ((JsonObject) answer.Body)["limit"] = _cfg.GetCVar(CustomMarkingCVars.LibraryLimit);
        return answer;
    }

    private static SymphonyCharactersSystem.Answer Cooldown(TimeSpan left)
    {
        var answer = Error(HttpStatusCode.TooManyRequests, "cooldown");
        ((JsonObject) answer.Body)["cooldown_ms"] = (int) Math.Ceiling(left.TotalMilliseconds);
        return answer;
    }

    private static SymphonyCharactersSystem.Answer Error(HttpStatusCode code, string error, string? detail = null)
    {
        return new SymphonyCharactersSystem.Answer(code, SymphonyJson.Error(error, detail));
    }

    private static JsonObject Entry(int id, string name, string hash, CustomMarkingPlacement placement)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["hash"] = hash,
            ["placement"] = placement.ToString(),
        };
    }

    private static CustomMarkingPlacement Placement(int stored)
    {
        var placement = (CustomMarkingPlacement) stored;
        return Enum.IsDefined(placement) ? placement : CustomMarkingPlacement.Skin;
    }

    /// <summary>Reads a base64 string; JSON null reads as null bytes and succeeds.</summary>
    private static bool TryBase64(JsonNode? node, out byte[]? bytes)
    {
        bytes = null;
        if (node == null)
            return true;

        try
        {
            bytes = Convert.FromBase64String(node.GetValue<string>());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
