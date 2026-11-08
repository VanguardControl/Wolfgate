using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Server._WF.FeatureBans;
using Content.Shared._WF.AppearanceStudio;
using Content.Shared._WF.Prototypes;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Symphony;
using Robust.Server.Player;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization.Manager;

namespace Content.Server.Symphony;

/// <summary>
/// The web character creator's view of a player's characters: list them, save one, or check one without saving.
/// SSymphony signs the player in with Discord and only ever names an account it has proven is theirs, so the user
/// id is trusted as given; everything else (validation, bans, the fields a web save can't touch) is decided here, as
/// in the game's own creator. Behind the admin API token. The contract is in this folder's README.
/// </summary>
public sealed class SymphonyCharactersSystem : EntitySystem
{
    public const string CharactersPath = "/symphony/characters";
    public const string ValidatePath = "/symphony/characters/validate";

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private IDependencyCollection _dependencies = default!;
    [Dependency] private FeatureBanSystem _featureBans = default!;

    /// <summary>A status code and a JSON body.</summary>
    public readonly record struct Answer(HttpStatusCode Code, JsonNode Body);

    public override void Initialize()
    {
        base.Initialize();
        _statusHost.AddHandler(HandleAsync);
    }

    // Runs on the status host's thread; the work itself hops to the main thread.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        var path = context.Url.AbsolutePath;
        if (path != CharactersPath && path != ValidatePath)
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
        Answer answer;
        try
        {
            if (path == CharactersPath && context.RequestMethod == HttpMethod.Get)
            {
                answer = await await _tasks.OnMainThread(() => List(user));
            }
            else if (path == CharactersPath && context.RequestMethod == HttpMethod.Put)
            {
                if (!int.TryParse(query["slot"], out var slot))
                {
                    await context.RespondJsonAsync(SymphonyJson.Error("bad_slot"), HttpStatusCode.BadRequest);
                    return true;
                }

                var body = await ReadBody(context);
                answer = await await _tasks.OnMainThread(() => Save(user, slot, body));
            }
            else if (path == ValidatePath && context.RequestMethod == HttpMethod.Post)
            {
                var body = await ReadBody(context);
                answer = await await _tasks.OnMainThread(() => Validate(user, body));
            }
            else
            {
                await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
                return true;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Symphony characters request {context.RequestMethod} {path} for {user} threw: {e}");
            answer = new Answer(HttpStatusCode.InternalServerError, SymphonyJson.Error("failed"));
        }

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

    private int MaxSlots => _cfg.GetCVar(CCVars.GameMaxCharacterSlots);

    /// <summary>The player's preferences: the live ones while they are connected, else as stored.</summary>
    private async Task<(PlayerPreferences? Prefs, ICommonSession? Session)> Load(NetUserId user)
    {
        if (_players.TryGetSessionById(user, out var session) && _prefs.GetPreferencesOrNull(user) is { } live)
            return (live, session);

        return (await _db.GetPlayerPreferencesAsync(user, CancellationToken.None), null);
    }

    /// <summary>GET /symphony/characters. Main thread.</summary>
    public async Task<Answer> List(NetUserId user)
    {
        var (prefs, session) = await Load(user);
        if (prefs == null)
            return new Answer(HttpStatusCode.NotFound, SymphonyJson.Error("no_preferences"));

        var slots = new JsonArray();
        foreach (var (slot, profile) in prefs.Characters.OrderBy(p => p.Key))
        {
            if (profile is HumanoidCharacterProfile humanoid)
                slots.Add(SlotJson(slot, humanoid));
        }

        return new Answer(HttpStatusCode.OK, new JsonObject
        {
            ["symphony_module"] = SharedSymphony.ModuleVersion,
            ["user"] = user.UserId.ToString(),
            ["online"] = session != null,
            ["selected"] = prefs.SelectedCharacterIndex,
            ["max_slots"] = MaxSlots,
            ["slots"] = slots,
        });
    }

    /// <summary>PUT /symphony/characters. Main thread.</summary>
    public async Task<Answer> Save(NetUserId user, int slot, JsonObject? body)
    {
        if (slot < 0 || slot >= MaxSlots)
            return new Answer(HttpStatusCode.BadRequest, SymphonyJson.Error("bad_slot"));

        if (body == null || !body.TryGetPropertyValue("profile", out var json) || json is not JsonObject)
            return new Answer(HttpStatusCode.BadRequest, SymphonyJson.Error("bad_request", "expected {\"etag\": ..., \"profile\": {...}}"));

        var etag = body["etag"]?.GetValue<string>();
        if (!TryParse(json, out var profile, out var error))
            return new Answer(HttpStatusCode.BadRequest, SymphonyJson.Error("bad_profile", error));

        if (await _db.GetServerBanAsync(null, user, null, null) != null)
            return new Answer(HttpStatusCode.Forbidden, SymphonyJson.Error("banned"));

        var (prefs, session) = await Load(user);
        if (prefs == null)
            return new Answer(HttpStatusCode.NotFound, SymphonyJson.Error("no_preferences"));

        var old = prefs.Characters.GetValueOrDefault(slot) as HumanoidCharacterProfile;
        var current = old == null ? null : SlotJson(slot, old);
        if (etag != current?["etag"]?.GetValue<string>())
            return new Answer(HttpStatusCode.Conflict, new JsonObject { ["error"] = "stale", ["current"] = current });

        var kept = new JsonArray();
        profile = Keep(await _featureBans.IsBannedAsync(user, StudioRules.Feature), profile, old, kept);

        HumanoidCharacterProfile saved;
        if (session != null)
        {
            // The game's own path for a client save: keeps the bank and persistent data, settles augments, and the
            // player's creator is sent the result.
            await _prefs.SetProfile(user, slot, profile, false);
            await _prefs.RefreshPreferencesAsync(session, CancellationToken.None);
            saved = _prefs.GetPreferences(user).Characters.GetValueOrDefault(slot) as HumanoidCharacterProfile ?? profile;
        }
        else
        {
            profile.EnsureValid(null!, _dependencies);
            await _db.SaveCharacterSlotAsync(user, profile, slot);
            saved = profile;
        }

        var answer = SlotJson(slot, saved);
        answer["kept"] = kept;
        Log.Info($"SSymphony saved slot {slot} of {user}");
        return new Answer(HttpStatusCode.OK, answer);
    }

    /// <summary>POST /symphony/characters/validate. Main thread.</summary>
    public async Task<Answer> Validate(NetUserId user, JsonObject? body)
    {
        if (body == null || !body.TryGetPropertyValue("profile", out var json) || json is not JsonObject)
            return new Answer(HttpStatusCode.BadRequest, SymphonyJson.Error("bad_request", "expected {\"profile\": {...}}"));

        if (!TryParse(json, out var profile, out var error))
            return new Answer(HttpStatusCode.BadRequest, SymphonyJson.Error("bad_profile", error));

        var (_, session) = await Load(user);
        profile = Keep(await _featureBans.IsBannedAsync(user, StudioRules.Feature), profile, null, new JsonArray());
        profile.EnsureValid(session!, _dependencies);
        return new Answer(HttpStatusCode.OK, new JsonObject { ["profile"] = ToJson(profile) });
    }

    /// <summary>
    /// Holds what a web save may not change at its stored values: the bank and persistent data (the game's save
    /// path does the same for a client), augments (bought in game), and, for a player banned from the Appearance
    /// Studio, its tweaks and custom markings. Each field held is named in <paramref name="kept"/>.
    /// </summary>
    private static HumanoidCharacterProfile Keep(bool studioBanned, HumanoidCharacterProfile profile, HumanoidCharacterProfile? old, JsonArray kept)
    {
        var bank = old?.BankBalance ?? HumanoidCharacterProfile.DefaultBalance;
        profile = profile.WithBankBalance(bank);
        if (old != null)
            profile = profile.WithPersistentData(old.Flags, old.Components, old.Items);
        else
            profile = profile.WithPersistentData(Array.Empty<string>(), Array.Empty<PersistentProfileComponent>(), Array.Empty<PersistentProfileItem>());

        kept.Add("bankBalance");
        kept.Add("persistentData");

        profile = profile
            .WithAugments(old?.Augments.ToDictionary() ?? new())
            .WithAugmentUnlocks(old?.AugmentUnlocks ?? Enumerable.Empty<Robust.Shared.Prototypes.ProtoId<Content.Shared._WF.Augments.WFAugmentPrototype>>());
        kept.Add("augments");

        if (studioBanned)
        {
            profile = profile
                .WithStudioTweaks(old?.StudioTweaks ?? new List<StudioTweak>())
                .WithCustomMarkings(old?.CustomMarkings ?? new List<Content.Shared._WF.CustomMarkings.CustomMarking>());
            kept.Add("studioTweaks");
            kept.Add("customMarkings");
        }

        return profile;
    }

    private bool TryParse(JsonNode json, out HumanoidCharacterProfile profile, out string? error)
    {
        try
        {
            profile = _serialization.Read<HumanoidCharacterProfile>(SymphonyJson.ToNode(json), notNullableOverride: true);
            WFLegacyPrototypeIds.ResolveProfile(profile);
            error = null;
            return true;
        }
        catch (Exception e)
        {
            profile = default!;
            error = e.Message;
            return false;
        }
    }

    private JsonNode? ToJson(HumanoidCharacterProfile profile)
    {
        return SymphonyJson.ToJson(_serialization.WriteValue(profile, alwaysWrite: true, notNullableOverride: true));
    }

    private JsonObject SlotJson(int slot, HumanoidCharacterProfile profile)
    {
        var json = ToJson(profile);
        return new JsonObject
        {
            ["slot"] = slot,
            ["etag"] = SymphonyJson.Fingerprint(json),
            ["profile"] = json,
        };
    }
}
