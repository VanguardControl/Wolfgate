using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WF.AppearanceStudio;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Symphony;
using Content.Shared.Traits;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;

namespace Content.Server.Symphony;

/// <summary>
/// What the web character creator draws with: a catalog of everything the creator offers (species, their body
/// sprites and doll layer order, markings, loadouts, traits, jobs, the studio's limits), the sprite files the
/// catalog names, and players' custom marking and warp art. The catalog is built once per build or prototype
/// reload and served from memory with its version as ETag. Behind the admin API token; contract in the README.
/// </summary>
public sealed class SymphonyCreatorSystem : EntitySystem
{
    public const string CatalogPath = "/symphony/creator/catalog";
    public const string RsiPath = "/symphony/creator/rsi";
    public const string ArtPath = "/symphony/creator/art";

    /// <summary>Components of a gear entity the web needs to draw it worn; the rest is left out of the catalog.</summary>
    private static readonly HashSet<string> GearComponents = new() { "Sprite", "Clothing", "Item" };

    private static readonly ResPath TextureRoot = new("/Textures");

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IServerDbManager _db = default!;

    /// <summary>The built catalog; replaced whole, so the status host's thread can read it without a lock.</summary>
    private volatile Catalog? _catalog;

    /// <summary>The built catalog: its version, its JSON and the RSI directories it names.</summary>
    public sealed record Catalog(string Version, byte[] Json, HashSet<string> Rsis);

    public override void Initialize()
    {
        base.Initialize();
        _statusHost.AddHandler(HandleAsync);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _catalog = null);
        Subs.CVar(_cfg, StudioCVars.MaxOffset, _ => _catalog = null);
        Subs.CVar(_cfg, StudioCVars.Warps, _ => _catalog = null);
    }

    // Runs on the status host's thread; only building the catalog hops to the main thread.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        var path = context.Url.AbsolutePath;
        if (path != CatalogPath && path != RsiPath && path != ArtPath)
            return false;

        if (!await SymphonyApi.AuthorisedAsync(_cfg, context))
            return true;

        if (context.RequestMethod != HttpMethod.Get)
        {
            await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
            return true;
        }

        var query = System.Web.HttpUtility.ParseQueryString(context.Url.Query);
        try
        {
            var catalog = _catalog ?? await _tasks.OnMainThread(Build);
            switch (path)
            {
                case CatalogPath:
                    await RespondCatalog(context, catalog, query["version"]);
                    break;
                case RsiPath:
                    await RespondRsi(context, catalog, query["path"]);
                    break;
                default:
                    await RespondArt(context, query["hash"]);
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Symphony creator request {path} threw: {e}");
            await context.RespondJsonAsync(SymphonyJson.Error("failed"), HttpStatusCode.InternalServerError);
        }

        return true;
    }

    private static bool Fresh(IStatusHandlerContext context, string version, string? asked)
    {
        return asked == version
               || context.RequestHeaders.TryGetValue("If-None-Match", out var match) && match.ToString().Trim('"') == version;
    }

    private static async Task RespondCatalog(IStatusHandlerContext context, Catalog catalog, string? version)
    {
        context.ResponseHeaders["ETag"] = $"\"{catalog.Version}\"";
        if (Fresh(context, catalog.Version, version))
        {
            await context.RespondAsync(Array.Empty<byte>(), HttpStatusCode.NotModified);
            return;
        }

        await context.RespondAsync(catalog.Json, HttpStatusCode.OK, "application/json");
    }

    private async Task RespondRsi(IStatusHandlerContext context, Catalog catalog, string? path)
    {
        // <dir>.rsi/<file>, with the file meta.json or a png, and the dir one the catalog names.
        var cut = path?.LastIndexOf('/') ?? -1;
        if (path == null || cut <= 0 || path.Contains("..") || path.Contains('\\'))
        {
            await context.RespondJsonAsync(SymphonyJson.Error("not_in_catalog"), HttpStatusCode.NotFound);
            return;
        }

        var dir = path[..cut].TrimStart('/');
        var file = path[(cut + 1)..];
        if (!catalog.Rsis.Contains(dir) || file != "meta.json" && !file.EndsWith(".png", StringComparison.Ordinal))
        {
            await context.RespondJsonAsync(SymphonyJson.Error("not_in_catalog"), HttpStatusCode.NotFound);
            return;
        }

        context.ResponseHeaders["ETag"] = $"\"{catalog.Version}\"";
        if (Fresh(context, catalog.Version, null))
        {
            await context.RespondAsync(Array.Empty<byte>(), HttpStatusCode.NotModified);
            return;
        }

        var resource = TextureRoot / dir / file;
        if (!_resources.TryContentFileRead(resource, out var stream))
        {
            await context.RespondJsonAsync(SymphonyJson.Error("missing"), HttpStatusCode.NotFound);
            return;
        }

        byte[] bytes;
        await using (stream)
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            bytes = copy.ToArray();
        }

        await context.RespondAsync(bytes, HttpStatusCode.OK, file == "meta.json" ? "application/json" : "image/png");
    }

    private async Task RespondArt(IStatusHandlerContext context, string? hash)
    {
        if (!CustomMarkingRules.IsValidHash(hash) || await _db.GetCustomMarkingArtAsync(hash!) is not { } art)
        {
            await context.RespondJsonAsync(SymphonyJson.Error("missing"), HttpStatusCode.NotFound);
            return;
        }

        if (CustomMarkingRules.UnpackFrameTimes(art.FrameTimes) is { } times)
        {
            context.ResponseHeaders["X-Frame-Times"] = string.Join(',', times);
            if (CustomMarkingRules.UnpackPingPong(art.FrameTimes))
                context.ResponseHeaders["X-Ping-Pong"] = "1";
        }

        context.ResponseHeaders["ETag"] = $"\"{hash}\"";
        await context.RespondAsync(art.Png, HttpStatusCode.OK, "image/png");
    }

    /// <summary>Builds the catalog from the loaded prototypes, or returns the one built already. Main thread only.</summary>
    public Catalog Build()
    {
        if (_catalog is { } built)
            return built;

        var prototypes = new JsonObject();
        var names = new JsonObject();
        var rsis = new HashSet<string>();

        AddKind<SpeciesPrototype>(prototypes);
        AddKind<HumanoidSpeciesBaseSpritesPrototype>(prototypes);
        AddKind<HumanoidSpeciesSpriteLayer>(prototypes);
        AddKind<MarkingPrototype>(prototypes);
        AddKind<MarkingPointsPrototype>(prototypes);
        AddKind<LoadoutPrototype>(prototypes);
        AddKind<LoadoutGroupPrototype>(prototypes);
        AddKind<RoleLoadoutPrototype>(prototypes);
        AddKind<StartingGearPrototype>(prototypes);
        AddKind<TraitPrototype>(prototypes);

        // Gear entities: every entity id a loadout or starting gear names, trimmed to what draws it.
        var entities = new JsonObject();
        var resolved = new Dictionary<string, JsonObject?>();
        var gear = new HashSet<string>();
        foreach (var kind in new[] { "loadout", "startingGear" })
        {
            if (prototypes[kind] is JsonObject entries)
                CollectStrings(entries, gear);
        }

        foreach (var id in gear.Where(id => _proto.HasIndex<EntityPrototype>(id)).Order())
        {
            if (ResolveEntity(id, resolved) is { } entity)
                entities[id] = TrimEntity(entity);
        }

        // Dolls: each species' doll entity and its sprite layers in draw order.
        var dolls = new JsonObject();
        var speciesNames = new JsonObject();
        foreach (var species in _proto.EnumeratePrototypes<SpeciesPrototype>().OrderBy(s => s.ID))
        {
            speciesNames[species.ID] = Loc.GetString(species.Name);
            if (ResolveEntity(species.DollPrototype.Id, resolved) is not { } doll)
                continue;

            entities[species.DollPrototype.Id] = TrimEntity(doll);
            dolls[species.ID] = new JsonObject
            {
                ["prototype"] = species.DollPrototype.Id,
                ["layers"] = SpriteLayers(doll),
            };
        }

        prototypes["entity"] = entities;

        names["species"] = speciesNames;
        names["marking"] = NamesOf<MarkingPrototype>(p => Loc.TryGetString($"marking-{p.ID}", out var n) ? n : p.ID);
        names["trait"] = NamesOf<TraitPrototype>(p => Loc.GetString(p.Name));
        names["job"] = NamesOf<JobPrototype>(p => p.LocalizedName);
        names["entity"] = new JsonObject(entities.Select(e =>
            new KeyValuePair<string, JsonNode?>(e.Key, _proto.TryIndex<EntityPrototype>(e.Key, out var ent) ? ent.Name : e.Key)));

        CollectRsis(prototypes, rsis);

        var catalog = new JsonObject
        {
            ["symphony_module"] = SharedSymphony.ModuleVersion,
            ["limits"] = new JsonObject
            {
                ["studio"] = new JsonObject
                {
                    ["enabled"] = _cfg.GetCVar(StudioCVars.Enabled),
                    ["maxOffset"] = _cfg.GetCVar(StudioCVars.MaxOffset),
                    ["minScale"] = _cfg.GetCVar(StudioCVars.MinScale),
                    ["maxScale"] = _cfg.GetCVar(StudioCVars.MaxScale),
                    ["warps"] = _cfg.GetCVar(StudioCVars.Warps),
                },
                ["customMarkings"] = new JsonObject
                {
                    ["enabled"] = _cfg.GetCVar(CustomMarkingCVars.Enabled),
                    ["maxWorn"] = _cfg.GetCVar(CustomMarkingCVars.MaxWorn),
                    ["maxFrames"] = _cfg.GetCVar(CustomMarkingCVars.MaxFrames),
                    ["eraseBody"] = _cfg.GetCVar(CustomMarkingCVars.EraseBody),
                    ["bodyReach"] = _cfg.GetCVar(CustomMarkingCVars.BodyReach),
                },
            },
            ["layers"] = new JsonArray(Enum.GetNames<HumanoidVisualLayers>().Select(n => (JsonNode?) n).ToArray()),
            ["dolls"] = dolls,
            ["prototypes"] = prototypes,
            ["names"] = names,
            ["rsis"] = new JsonArray(rsis.Order().Select(r => (JsonNode?) r).ToArray()),
        };

        // The version is the fingerprint of everything else, so it changes exactly when the content does.
        var version = SymphonyJson.Fingerprint(Encoding.UTF8.GetBytes(catalog.ToJsonString()));
        catalog["version"] = version;

        var result = new Catalog(version, Encoding.UTF8.GetBytes(catalog.ToJsonString()), rsis);
        _catalog = result;
        Log.Info($"Built the web creator catalog {version}: {result.Json.Length / 1024} KiB, {rsis.Count} RSIs");
        return result;
    }

    private void AddKind<T>(JsonObject prototypes) where T : class, IPrototype
    {
        if (!_proto.TryGetKindFrom<T>(out var kind))
            return;

        var entries = new JsonObject();
        foreach (var proto in _proto.EnumeratePrototypes<T>().OrderBy(p => p.ID))
        {
            if (_proto.TryGetMapping(typeof(T), proto.ID, out var mapping))
                entries[proto.ID] = SymphonyJson.ToJson(mapping);
        }

        prototypes[kind] = entries;
    }

    private JsonObject NamesOf<T>(Func<T, string> name) where T : class, IPrototype
    {
        var names = new JsonObject();
        foreach (var proto in _proto.EnumeratePrototypes<T>().OrderBy(p => p.ID))
        {
            names[proto.ID] = name(proto);
        }

        return names;
    }

    /// <summary>
    /// An entity's mapping with its parents' components merged in, as the game builds the prototype: each parent in
    /// turn, then the entity's own, a component's fields overriding the same component's from before. The stored
    /// mappings are as written, so a doll whose sprite comes from its parent would otherwise have none.
    /// </summary>
    private JsonObject? ResolveEntity(string id, Dictionary<string, JsonObject?> resolved)
    {
        if (resolved.TryGetValue(id, out var known))
            return known;

        resolved[id] = null; // A cycle resolves to nothing rather than recursing forever.
        if (!_proto.TryGetMapping(typeof(EntityPrototype), id, out var mapping) || SymphonyJson.ToJson(mapping) is not JsonObject own)
            return null;

        var components = new Dictionary<string, JsonObject>();
        var order = new List<string>();
        var name = own["name"]?.DeepClone();
        foreach (var parent in Parents(own["parent"]))
        {
            if (ResolveEntity(parent, resolved) is not { } inherited)
                continue;

            name ??= inherited["name"]?.DeepClone();
            MergeComponents(inherited["components"], components, order);
        }

        MergeComponents(own["components"], components, order);

        var result = new JsonObject
        {
            ["id"] = id,
            ["parent"] = own["parent"]?.DeepClone(),
            ["name"] = name,
            ["components"] = new JsonArray(order.Select(type => (JsonNode?) components[type].DeepClone()).ToArray()),
        };

        resolved[id] = result;
        return result;
    }

    private static IEnumerable<string> Parents(JsonNode? parent)
    {
        switch (parent)
        {
            case JsonValue value when value.TryGetValue<string>(out var single):
                yield return single;
                break;
            case JsonArray many:
                foreach (var item in many)
                {
                    if (item is JsonValue entry && entry.TryGetValue<string>(out var text))
                        yield return text;
                }

                break;
        }
    }

    private static void MergeComponents(JsonNode? list, Dictionary<string, JsonObject> components, List<string> order)
    {
        if (list is not JsonArray array)
            return;

        foreach (var node in array)
        {
            if (node is not JsonObject component || component["type"]?.GetValue<string>() is not { } type)
                continue;

            if (!components.TryGetValue(type, out var existing))
            {
                components[type] = (JsonObject) component.DeepClone();
                order.Add(type);
                continue;
            }

            MergeInto(existing, component);
        }
    }

    /// <summary>Copies a child mapping's fields over its parent's, merging nested mappings and replacing anything else.</summary>
    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject child && target[key] is JsonObject parent)
                MergeInto(parent, child);
            else
                target[key] = value?.DeepClone();
        }
    }

    /// <summary>An entity's mapping with only its id, parent, name and the components that draw it.</summary>
    private static JsonNode TrimEntity(JsonObject json)
    {
        var trimmed = new JsonObject();
        foreach (var key in new[] { "id", "parent", "name" })
        {
            if (json[key] is { } value)
                trimmed[key] = value.DeepClone();
        }

        var components = new JsonArray();
        if (json["components"] is JsonArray all)
        {
            foreach (var component in all)
            {
                if (component?["type"]?.GetValue<string>() is { } type && GearComponents.Contains(type))
                    components.Add(component.DeepClone());
            }
        }

        trimmed["components"] = components;
        return trimmed;
    }

    /// <summary>The layers of an entity's Sprite component, in draw order.</summary>
    private static JsonNode SpriteLayers(JsonObject json)
    {
        if (json["components"] is JsonArray components)
        {
            foreach (var component in components)
            {
                if (component?["type"]?.GetValue<string>() == "Sprite" && component["layers"] is JsonArray layers)
                    return layers.DeepClone();
            }
        }

        return new JsonArray();
    }

    private static void CollectStrings(JsonNode? node, HashSet<string> into)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, value) in obj)
                {
                    CollectStrings(value, into);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    CollectStrings(item, into);
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                into.Add(text);
                break;
        }
    }

    /// <summary>Every .rsi directory the catalog names, relative to /Textures.</summary>
    private static void CollectRsis(JsonNode? node, HashSet<string> rsis)
    {
        var strings = new HashSet<string>();
        CollectStrings(node, strings);
        foreach (var text in strings)
        {
            if (!text.EndsWith(".rsi", StringComparison.Ordinal))
                continue;

            var path = text.TrimStart('/');
            if (path.StartsWith("Textures/", StringComparison.Ordinal))
                path = path["Textures/".Length..];

            rsis.Add(path);
        }
    }
}
