using System;
using System.Collections.Concurrent;
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
using Content.Shared.Preferences.Loadouts.Effects;
using Robust.Shared.Prototypes;
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
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Largest save or validate body accepted, in bytes; a profile is a few KB.</summary>
    public const int MaxBodyBytes = 256 * 1024;

    /// <summary>Accounts with a save or validate in flight.</summary>
    private readonly ConcurrentDictionary<NetUserId, byte> _busy = new();

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
            else if (path == CharactersPath && context.RequestMethod == HttpMethod.Put
                     || path == ValidatePath && context.RequestMethod == HttpMethod.Post)
            {
                var saving = path == CharactersPath;
                int? slot = int.TryParse(query["slot"], out var parsedSlot) ? parsedSlot : null;
                if (saving && slot == null)
                {
                    await context.RespondJsonAsync(SymphonyJson.Error("bad_slot"), HttpStatusCode.BadRequest);
                    return true;
                }

                var (body, tooLarge) = await SymphonyApi.ReadJsonBodyAsync(context, MaxBodyBytes);
                if (tooLarge)
                {
                    await context.RespondJsonAsync(SymphonyJson.Error("too_large", $"at most {MaxBodyBytes} bytes"), HttpStatusCode.RequestEntityTooLarge);
                    return true;
                }

                // One save or validate per account at a time, so a burst can't queue work on the game thread.
                if (!_busy.TryAdd(user, 0))
                {
                    await context.RespondJsonAsync(SymphonyJson.Error("busy"), HttpStatusCode.TooManyRequests);
                    return true;
                }

                try
                {
                    // Reading the profile only looks prototypes up, so it runs here, off the game thread.
                    if (ParseRequest(body, saving, out var etag, out var profile) is { } refused)
                        answer = refused;
                    else if (saving)
                        answer = await await _tasks.OnMainThread(() => SaveProfile(user, slot!.Value, etag, profile));
                    else
                        answer = await await _tasks.OnMainThread(() => ValidateProfile(user, profile, slot));
                }
                finally
                {
                    _busy.TryRemove(user, out _);
                }
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

    /// <summary>
    /// Pulls the profile (and, for a save, the etag) out of a request body. Returns the refusal, or null when the
    /// profile was read. Safe off the game thread: it only reads prototypes.
    /// </summary>
    private Answer? ParseRequest(JsonObject? body, bool saving, out string? etag, out HumanoidCharacterProfile profile)
    {
        etag = null;
        profile = default!;
        if (body == null || !body.TryGetPropertyValue("profile", out var json) || json is not JsonObject)
        {
            return new Answer(HttpStatusCode.BadRequest, SymphonyJson.Error("bad_request",
                saving ? "expected {\"etag\": ..., \"profile\": {...}}" : "expected {\"profile\": {...}}"));
        }

        if (saving)
        {
            if (body["etag"] is JsonValue tag && tag.TryGetValue<string>(out var text))
                etag = text;
            else if (body["etag"] != null)
                return new Answer(HttpStatusCode.BadRequest, SymphonyJson.Error("bad_request", "etag must be a string or null"));
        }

        return TryParse(json, out profile, out var error)
            ? null
            : new Answer(HttpStatusCode.BadRequest, SymphonyJson.Error("bad_profile", error));
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

    /// <summary>PUT /symphony/characters, body and all. Main thread.</summary>
    public async Task<Answer> Save(NetUserId user, int slot, JsonObject? body)
    {
        return ParseRequest(body, true, out var etag, out var profile) ?? await SaveProfile(user, slot, etag, profile);
    }

    /// <summary>PUT /symphony/characters with the profile already read. Main thread.</summary>
    private async Task<Answer> SaveProfile(NetUserId user, int slot, string? etag, HumanoidCharacterProfile profile)
    {
        if (slot < 0 || slot >= MaxSlots)
            return new Answer(HttpStatusCode.BadRequest, SymphonyJson.Error("bad_slot"));

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
        var droppedArt = new JsonArray();
        profile = await DropMissingArt(profile, droppedArt);

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
            await EnsureValidOffline(user, profile);
            await _db.SaveCharacterSlotAsync(user, profile, slot);
            saved = profile;
        }

        var answer = SlotJson(slot, saved);
        answer["kept"] = kept;
        answer["dropped_art"] = droppedArt;
        Log.Info($"SSymphony saved slot {slot} of {user}");
        return new Answer(HttpStatusCode.OK, answer);
    }

    /// <summary>POST /symphony/characters/validate, body and all. Main thread.</summary>
    public async Task<Answer> Validate(NetUserId user, JsonObject? body, int? slot = null)
    {
        return ParseRequest(body, false, out _, out var profile) ?? await ValidateProfile(user, profile, slot);
    }

    /// <summary>POST /symphony/characters/validate with the profile already read. Main thread.</summary>
    private async Task<Answer> ValidateProfile(NetUserId user, HumanoidCharacterProfile profile, int? slot)
    {
        // Held fields come from the slot being edited, as a save would take them; an empty or unnamed slot uses defaults.
        var (prefs, session) = await Load(user);
        var old = slot is { } index ? prefs?.Characters.GetValueOrDefault(index) as HumanoidCharacterProfile : null;
        profile = Keep(await _featureBans.IsBannedAsync(user, StudioRules.Feature), profile, old, new JsonArray());
        var droppedArt = new JsonArray();
        profile = await DropMissingArt(profile, droppedArt);
        if (session != null)
            profile.EnsureValid(session, _dependencies);
        else
            await EnsureValidOffline(user, profile);

        return new Answer(HttpStatusCode.OK, new JsonObject { ["profile"] = ToJson(profile), ["dropped_art"] = droppedArt });
    }

    /// <summary>
    /// EnsureValid for an account that isn't connected. Without a session, loadout job requirements (playtime) pass
    /// unchecked, so they are checked here against the stored play times instead, and what fails is taken off; a
    /// second pass refills any group left under its minimum with its defaults.
    /// </summary>
    private async Task EnsureValidOffline(NetUserId user, HumanoidCharacterProfile profile)
    {
        profile.EnsureValid(null!, _dependencies);

        var times = new Dictionary<string, TimeSpan>();
        foreach (var time in await _db.GetPlayTimes(user))
        {
            times[time.Tracker] = time.TimeSpent;
        }

        var stripped = false;
        foreach (var roleLoadout in profile.Loadouts.Values)
        {
            foreach (var (_, loadouts) in roleLoadout.SelectedLoadouts)
            {
                foreach (var loadout in loadouts.ToArray())
                {
                    if (!_proto.TryIndex(loadout.Prototype, out var proto)
                        || Earned(proto.Effects, profile, times) && Earned(proto.HideEffects, profile, times))
                        continue;

                    // Straight off the list: RemoveLoadout refuses to go under a group's minimum, which the
                    // second EnsureValid then fills with defaults.
                    loadouts.Remove(loadout);
                    stripped = true;
                }
            }
        }

        if (stripped)
            profile.EnsureValid(null!, _dependencies);
    }

    /// <summary>Whether every job requirement among a loadout's effects (and their effect groups) is met.</summary>
    private bool Earned(IEnumerable<LoadoutEffect> effects, HumanoidCharacterProfile profile, Dictionary<string, TimeSpan> times, int depth = 0)
    {
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case JobRequirementLoadoutEffect job when !job.Requirement.Check(EntityManager, _proto, profile, times, out _):
                    return false;
                case GroupLoadoutEffect group when depth < 8
                                                   && _proto.TryIndex(group.Proto, out var groupProto)
                                                   && !Earned(groupProto.Effects, profile, times, depth + 1):
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Takes off any custom marking or studio warp whose art the server doesn't hold or an admin blocked, naming
    /// each dropped hash in <paramref name="dropped"/>. A web save can only point at art that is really there.
    /// </summary>
    private async Task<HumanoidCharacterProfile> DropMissingArt(HumanoidCharacterProfile profile, JsonArray dropped)
    {
        var hashes = profile.CustomMarkings.Select(m => m.Hash)
            .Concat(StudioRules.Warps(profile.StudioTweaks))
            .Distinct()
            .ToList();

        var gone = new HashSet<string>();
        foreach (var hash in hashes)
        {
            if (!Content.Shared._WF.CustomMarkings.CustomMarkingRules.IsValidHash(hash) || await _db.GetCustomMarkingArtAsync(hash) == null)
                gone.Add(hash);
        }

        if (gone.Count == 0)
            return profile;

        foreach (var hash in gone)
        {
            dropped.Add(hash);
        }

        var tweaks = new List<StudioTweak>();
        foreach (var tweak in profile.StudioTweaks)
        {
            var copy = tweak.Clone();
            if (copy.Warp != null && gone.Contains(copy.Warp))
                copy.Warp = null;

            tweaks.Add(copy);
        }

        return profile
            .WithCustomMarkings(profile.CustomMarkings.Where(m => !gone.Contains(m.Hash)).ToList())
            .WithStudioTweaks(tweaks);
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
