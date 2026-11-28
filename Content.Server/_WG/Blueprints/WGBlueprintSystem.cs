using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._WG.PlanetPois;
using Content.Server.Power.EntitySystems;
using Content.Server.Research.Components;
using Content.Server.Research.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared._WG.Blueprints;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Research.Components;
using Content.Server.Administration.Logs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Blueprints;

/// <summary>
/// The Blueprint Artifact System: the locator lists patterns and starts hunts, hunts hide shards at planet sites, the
/// research console decodes shards into Constructs and a Construct unlocks its pattern on the console's research server..
/// </summary>
public sealed partial class WGBlueprintSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WGPlanetPoiSystem _pois = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WGBlueprintLocatorComponent, BoundUIOpenedEvent>(OnLocatorOpened);
        SubscribeLocalEvent<WGBlueprintLocatorComponent, WGBlueprintLocateMessage>(OnLocate);
        SubscribeLocalEvent<WGBlueprintLocatorComponent, WGBlueprintAbandonMessage>(OnAbandon);
        SubscribeLocalEvent<WGBlueprintLocatorComponent, ResearchServerPointsChangedEvent>(OnResearchChanged);
        SubscribeLocalEvent<WGBlueprintLocatorComponent, ResearchRegistrationChangedEvent>(OnResearchChanged);
        SubscribeLocalEvent<WGBlueprintLocatorComponent, TechnologyDatabaseModifiedEvent>(OnResearchChanged);
        SubscribeLocalEvent<WGBlueprintLocatorComponent, WGBlueprintDecodeDoAfterEvent>(OnDecoded);
        SubscribeLocalEvent<WGBlueprintLocatorComponent, InteractUsingEvent>(OnLocatorInteractUsing);
        SubscribeLocalEvent<WGBlueprintLocatorComponent, ComponentShutdown>(OnLocatorShutdown);

        SubscribeLocalEvent<WGBlueprintShardComponent, ComponentShutdown>(OnShardShutdown);
        SubscribeLocalEvent<WGBlueprintShardComponent, ExaminedEvent>(OnShardExamined);
        SubscribeLocalEvent<WGBlueprintConstructComponent, ExaminedEvent>(OnConstructExamined);
    }

    private void OnLocatorOpened(Entity<WGBlueprintLocatorComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is WGBlueprintLocatorUiKey)
            UpdateUi(ent);
    }

    private void OnResearchChanged<T>(Entity<WGBlueprintLocatorComponent> ent, ref T args)
    {
        if (_ui.IsUiOpen(ent.Owner, WGBlueprintLocatorUiKey.Key))
            UpdateUi(ent);
    }

    /// <summary>Whether every prerequisite of the pattern's technology is known; a relic pattern has none.</summary>
    public bool PrerequisitesKnown(EntityUid locator, WGBlueprintPatternPrototype pattern)
    {
        if (pattern.Technology is not { } techId || !_proto.TryIndex(techId, out var tech))
            return true;

        if (!TryGetDatabase(locator, out _, out var db))
            return false;

        var techs = db.UnlockedTechnologies;
        return tech.TechnologyPrerequisites.All(p => techs.Contains(p));
    }

    /// <summary>Whether the locator's server already holds the pattern's technology and every recipe.</summary>
    public bool IsOwned(EntityUid locator, WGBlueprintPatternPrototype pattern)
    {
        if (!TryGetDatabase(locator, out _, out var db))
            return false;

        var techs = db.UnlockedTechnologies;
        if (pattern.Technology is { } tech && !techs.Contains(tech))
            return false;

        var unlocked = db.UnlockedRecipes;
        foreach (var recipe in pattern.Recipes)
        {
            if (!unlocked.Contains(recipe))
                return false;
        }

        return true;
    }

    /// <summary>Powered decoding arrays (research datafarms) linked to the locator's server.</summary>
    public int CountArrays(EntityUid locator)
    {
        if (!_research.TryGetClientServer(locator, out _, out var server))
            return 0;

        var arrays = 0;
        foreach (var client in server.Clients)
        {
            if (!HasComp<ResearchServerComponent>(client)
                && TryComp<ResearchPointSourceComponent>(client, out var source)
                && _research.CanProduce((client, source)))
                arrays++;
        }

        return arrays;
    }

    private bool TryGetDatabase(EntityUid locator, out EntityUid server, out TechnologyDatabaseComponent db)
    {
        server = default;
        db = default!;
        if (!_research.TryGetClientServer(locator, out var serverUid, out _)
            || !TryComp<TechnologyDatabaseComponent>(serverUid, out var found))
            return false;

        server = serverUid.Value;
        db = found;
        return true;
    }

    public void UpdateUi(Entity<WGBlueprintLocatorComponent> ent)
    {
        var linked = TryGetDatabase(ent, out _, out var db);
        var patterns = new List<WGBlueprintPatternState>();
        foreach (var pattern in _proto.EnumeratePrototypes<WGBlueprintPatternPrototype>().OrderBy(p => p.Tier).ThenBy(p => p.ID))
        {
            if (Fits(ent, pattern))
                patterns.Add(new WGBlueprintPatternState(pattern.ID, IsOwned(ent, pattern), PrerequisitesKnown(ent, pattern)));
        }

        var known = new HashSet<string>();
        var disciplines = new List<string>();
        if (linked)
        {
            var techs = db.UnlockedTechnologies;
            var supported = db.SupportedDisciplines;
            known.UnionWith(techs);
            disciplines.AddRange(supported);
        }

        WGBlueprintHuntState? hunt = null;
        if (ent.Comp.Hunt is { } active && TryComp<WGPlanetPoiComponent>(active.Ground, out var record)
            && active.SiteIndex < record.Sites.Count)
        {
            hunt = new WGBlueprintHuntState(
                Loc.GetString(record.Sites[active.SiteIndex].Name),
                Name(active.Ground),
                active.Tier,
                active.Patterns.Select(p => p.Id).ToList(),
                active.Shards.Count);
        }

        var hasServer = _research.TryGetClientServer(ent, out _, out var server);
        _ui.SetUiState(ent.Owner, WGBlueprintLocatorUiKey.Key,
            new WGBlueprintLocatorState(linked, hasServer ? server!.ServerName : null, patterns, ent.Comp.Fees,
                ent.Comp.MaxPatterns, hunt, ent.Comp.NextHunt, hasServer ? Math.Max(0, server!.Points) : 0, known, disciplines,
                CountArrays(ent)));
    }

    private void OnLocatorInteractUsing(Entity<WGBlueprintLocatorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp<WGBlueprintConstructComponent>(args.Used, out var construct))
        {
            args.Handled = true;
            TryInsertConstruct(ent, (args.Used, construct), args.User);
        }
        else if (TryComp<WGBlueprintShardComponent>(args.Used, out var shard))
        {
            args.Handled = true;
            TryStartDecode(ent, (args.Used, shard), args.User);
        }
    }

    /// <summary>Unlocks a Construct's pattern on the locator's server and consumes it.</summary>
    public bool TryInsertConstruct(Entity<WGBlueprintLocatorComponent> ent, Entity<WGBlueprintConstructComponent> construct, EntityUid user)
    {
        if (construct.Comp.Pattern is not { } patternId || !_proto.TryIndex(patternId, out var pattern))
            return false;

        if (!TryGetDatabase(ent, out var server, out _))
        {
            _popup.PopupEntity(Loc.GetString("wg-blueprint-locator-unlinked"), ent, user, PopupType.MediumCaution);
            return false;
        }

        if (IsOwned(ent, pattern))
        {
            _popup.PopupEntity(Loc.GetString("wg-blueprint-construct-owned"), ent, user);
            return false;
        }

        Unlock(server, pattern);
        QueueDel(construct);
        _popup.PopupEntity(Loc.GetString("wg-blueprint-construct-unlocked", ("pattern", Loc.GetString(pattern.Name))), ent, user);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):player} unlocked blueprint pattern {pattern.ID} at {ToPrettyString(ent)} for server {ToPrettyString(server)}");
        UpdateUi(ent);
        return true;
    }

    /// <summary>
    /// Whether the locator's server can use the pattern: a technology's pattern needs one of its disciplines, so a
    /// faction's technology is only hunted from that faction's servers.
    /// </summary>
    public bool Fits(EntityUid locator, WGBlueprintPatternPrototype pattern)
    {
        if (pattern.Technology is not { } techId || !_proto.TryIndex(techId, out var tech))
            return true;

        if (!TryGetDatabase(locator, out _, out var db))
            return false;

        var supported = db.SupportedDisciplines;
        foreach (var discipline in tech.GetAllDisciplines())
        {
            if (supported.Contains(discipline))
                return true;
        }

        return false;
    }

    /// <summary>Adds the pattern's technology and every recipe to the server's database.</summary>
    public void Unlock(EntityUid server, WGBlueprintPatternPrototype pattern)
    {
        if (pattern.Technology is { } tech && _proto.TryIndex(tech, out var technology))
        {
            _research.AddTechnology(server, technology);
            _research.UpdateTechnologyCards(server);
        }

        foreach (var recipe in pattern.Recipes)
        {
            _research.AddLatheRecipe(server, recipe);
        }
    }

    private void OnShardExamined(Entity<WGBlueprintShardComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Pattern is { } id && _proto.TryIndex(id, out var pattern))
            args.PushMarkup(Loc.GetString("wg-blueprint-shard-examine", ("pattern", Loc.GetString(pattern.Name)), ("tier", pattern.Tier)));
    }

    private void OnConstructExamined(Entity<WGBlueprintConstructComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Pattern is { } id && _proto.TryIndex(id, out var pattern))
            args.PushMarkup(Loc.GetString("wg-blueprint-construct-examine", ("pattern", Loc.GetString(pattern.Name))));
    }

    /// <summary>Makes a shard or Construct item for a pattern and names it after the pattern.</summary>
    public EntityUid SpawnFor(EntProtoId proto, WGBlueprintPatternPrototype pattern, EntityCoordinates coords, string nameKey)
    {
        var uid = Spawn(proto, coords);
        if (TryComp<WGBlueprintShardComponent>(uid, out var shard))
            shard.Pattern = pattern.ID;
        if (TryComp<WGBlueprintConstructComponent>(uid, out var construct))
            construct.Pattern = pattern.ID;

        _meta.SetEntityName(uid, Loc.GetString(nameKey, ("pattern", Loc.GetString(pattern.Name))));
        return uid;
    }

    /// <summary>Starts reading a shard at the console; the user holds it until it is sealed into a Construct.</summary>
    private void TryStartDecode(Entity<WGBlueprintLocatorComponent> ent, Entity<WGBlueprintShardComponent> shard, EntityUid user)
    {
        if (shard.Comp.Pattern is not { } id || !_proto.TryIndex(id, out var pattern))
            return;

        if (!_power.IsPowered(ent))
        {
            _popup.PopupEntity(Loc.GetString("wg-blueprint-decode-unpowered"), ent, user);
            return;
        }

        var time = ent.Comp.DecodeTimes[Math.Clamp(pattern.Tier - 1, 0, ent.Comp.DecodeTimes.Length - 1)] * WGBlueprintDecode.Scale(CountArrays(ent));
        var doAfter = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(time), new WGBlueprintDecodeDoAfterEvent(), ent, ent, shard)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnDropItem = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("wg-blueprint-decode-start"), ent, user);
    }

    private void OnDecoded(Entity<WGBlueprintLocatorComponent> ent, ref WGBlueprintDecodeDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } used
            || !TryComp<WGBlueprintShardComponent>(used, out var shard)
            || shard.Pattern is not { } id || !_proto.TryIndex(id, out var pattern))
            return;

        args.Handled = true;
        Decode(ent, (used, shard), pattern, args.User);
        _popup.PopupEntity(Loc.GetString("wg-blueprint-decode-done", ("pattern", Loc.GetString(pattern.Name))), ent, args.User);
    }

    /// <summary>Replaces a shard with its pattern's Construct, handed to the user.</summary>
    public EntityUid Decode(Entity<WGBlueprintLocatorComponent> ent, Entity<WGBlueprintShardComponent> shard, WGBlueprintPatternPrototype pattern, EntityUid? user)
    {
        var construct = SpawnFor(ent.Comp.Construct, pattern, Transform(ent).Coordinates, "wg-blueprint-construct-name");
        QueueDel(shard);
        if (user != null)
            _hands.PickupOrDrop(user, construct);
        return construct;
    }
}
