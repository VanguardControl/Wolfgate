using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._WF.PlanetPois;
using Content.Server.Power.EntitySystems;
using Content.Server.Research.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared._WF.Blueprints;
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

namespace Content.Server._WF.Blueprints;

/// <summary>
/// The Blueprint Artifact System: the locator lists patterns and starts hunts, hunts hide shards at planet sites, the
/// assembler turns shards into Constructs and a Construct unlocks its pattern on the locator's research server.
/// </summary>
public sealed partial class WFBlueprintSystem : EntitySystem
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
    [Dependency] private WFPlanetPoiSystem _pois = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFBlueprintLocatorComponent, BoundUIOpenedEvent>(OnLocatorOpened);
        SubscribeLocalEvent<WFBlueprintLocatorComponent, WFBlueprintLocateMessage>(OnLocate);
        SubscribeLocalEvent<WFBlueprintLocatorComponent, WFBlueprintAbandonMessage>(OnAbandon);
        SubscribeLocalEvent<WFBlueprintLocatorComponent, InteractUsingEvent>(OnLocatorInteractUsing);
        SubscribeLocalEvent<WFBlueprintLocatorComponent, ComponentShutdown>(OnLocatorShutdown);

        SubscribeLocalEvent<WFBlueprintShardComponent, ComponentShutdown>(OnShardShutdown);
        SubscribeLocalEvent<WFBlueprintShardComponent, ExaminedEvent>(OnShardExamined);
        SubscribeLocalEvent<WFBlueprintConstructComponent, ExaminedEvent>(OnConstructExamined);

        SubscribeLocalEvent<WFBlueprintAssemblerComponent, InteractUsingEvent>(OnAssemblerInteractUsing);
        SubscribeLocalEvent<WFBlueprintAssemblerComponent, WFBlueprintAssembleDoAfterEvent>(OnAssembled);
    }

    private void OnLocatorOpened(Entity<WFBlueprintLocatorComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is WFBlueprintLocatorUiKey)
            UpdateUi(ent);
    }

    /// <summary>Whether the locator's server already holds every recipe of the pattern.</summary>
    public bool IsOwned(EntityUid locator, WFBlueprintPatternPrototype pattern)
    {
        if (!TryGetDatabase(locator, out _, out var db))
            return false;

        var unlocked = db.UnlockedRecipes;
        foreach (var recipe in pattern.Recipes)
        {
            if (!unlocked.Contains(recipe))
                return false;
        }

        return true;
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

    public void UpdateUi(Entity<WFBlueprintLocatorComponent> ent)
    {
        var linked = TryGetDatabase(ent, out _, out _);
        var patterns = new List<WFBlueprintPatternState>();
        foreach (var pattern in _proto.EnumeratePrototypes<WFBlueprintPatternPrototype>().OrderBy(p => p.Tier).ThenBy(p => p.ID))
        {
            patterns.Add(new WFBlueprintPatternState(pattern.ID, IsOwned(ent, pattern)));
        }

        WFBlueprintHuntState? hunt = null;
        if (ent.Comp.Hunt is { } active && TryComp<WFPlanetPoiComponent>(active.Ground, out var record)
            && active.SiteIndex < record.Sites.Count)
        {
            hunt = new WFBlueprintHuntState(
                Loc.GetString(record.Sites[active.SiteIndex].Name),
                Name(active.Ground),
                active.Tier,
                active.Patterns.Select(p => p.Id).ToList(),
                active.Shards.Count);
        }

        _ui.SetUiState(ent.Owner, WFBlueprintLocatorUiKey.Key,
            new WFBlueprintLocatorState(linked, patterns, ent.Comp.Fees, ent.Comp.MaxPatterns, hunt, ent.Comp.NextHunt));
    }

    private void OnLocatorInteractUsing(Entity<WFBlueprintLocatorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<WFBlueprintConstructComponent>(args.Used, out var construct))
            return;

        args.Handled = true;
        TryInsertConstruct(ent, (args.Used, construct), args.User);
    }

    /// <summary>Unlocks a Construct's pattern on the locator's server and consumes it.</summary>
    public bool TryInsertConstruct(Entity<WFBlueprintLocatorComponent> ent, Entity<WFBlueprintConstructComponent> construct, EntityUid user)
    {
        if (construct.Comp.Pattern is not { } patternId || !_proto.TryIndex(patternId, out var pattern))
            return false;

        if (!TryGetDatabase(ent, out var server, out _))
        {
            _popup.PopupEntity(Loc.GetString("wf-blueprint-locator-unlinked"), ent, user, PopupType.MediumCaution);
            return false;
        }

        if (IsOwned(ent, pattern))
        {
            _popup.PopupEntity(Loc.GetString("wf-blueprint-construct-owned"), ent, user);
            return false;
        }

        Unlock(server, pattern);
        QueueDel(construct);
        _popup.PopupEntity(Loc.GetString("wf-blueprint-construct-unlocked", ("pattern", Loc.GetString(pattern.Name))), ent, user);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):player} unlocked blueprint pattern {pattern.ID} at {ToPrettyString(ent)} for server {ToPrettyString(server)}");
        UpdateUi(ent);
        return true;
    }

    /// <summary>Adds every recipe of the pattern to the server's database.</summary>
    public void Unlock(EntityUid server, WFBlueprintPatternPrototype pattern)
    {
        foreach (var recipe in pattern.Recipes)
        {
            _research.AddLatheRecipe(server, recipe);
        }
    }

    private void OnShardExamined(Entity<WFBlueprintShardComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Pattern is { } id && _proto.TryIndex(id, out var pattern))
            args.PushMarkup(Loc.GetString("wf-blueprint-shard-examine", ("pattern", Loc.GetString(pattern.Name)), ("tier", pattern.Tier)));
    }

    private void OnConstructExamined(Entity<WFBlueprintConstructComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Pattern is { } id && _proto.TryIndex(id, out var pattern))
            args.PushMarkup(Loc.GetString("wf-blueprint-construct-examine", ("pattern", Loc.GetString(pattern.Name))));
    }

    /// <summary>Makes a shard or Construct item for a pattern and names it after the pattern.</summary>
    public EntityUid SpawnFor(EntProtoId proto, WFBlueprintPatternPrototype pattern, EntityCoordinates coords, string nameKey)
    {
        var uid = Spawn(proto, coords);
        if (TryComp<WFBlueprintShardComponent>(uid, out var shard))
            shard.Pattern = pattern.ID;
        if (TryComp<WFBlueprintConstructComponent>(uid, out var construct))
            construct.Pattern = pattern.ID;

        _meta.SetEntityName(uid, Loc.GetString(nameKey, ("pattern", Loc.GetString(pattern.Name))));
        return uid;
    }

    private void OnAssemblerInteractUsing(Entity<WFBlueprintAssemblerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<WFBlueprintShardComponent>(args.Used, out var shard))
            return;

        args.Handled = true;
        if (shard.Pattern is not { } id || !_proto.TryIndex(id, out var pattern))
            return;

        if (!_power.IsPowered(ent))
        {
            _popup.PopupEntity(Loc.GetString("wf-blueprint-assembler-unpowered"), ent, args.User);
            return;
        }

        var time = ent.Comp.Times[Math.Clamp(pattern.Tier - 1, 0, ent.Comp.Times.Length - 1)];
        var doAfter = new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(time), new WFBlueprintAssembleDoAfterEvent(), ent, ent, args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnDropItem = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("wf-blueprint-assembler-start"), ent, args.User);
    }

    private void OnAssembled(Entity<WFBlueprintAssemblerComponent> ent, ref WFBlueprintAssembleDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } used
            || !TryComp<WFBlueprintShardComponent>(used, out var shard)
            || shard.Pattern is not { } id || !_proto.TryIndex(id, out var pattern))
            return;

        args.Handled = true;
        var construct = SpawnFor(ent.Comp.Construct, pattern, Transform(ent).Coordinates, "wf-blueprint-construct-name");
        QueueDel(used);
        _hands.PickupOrDrop(args.User, construct);
        _popup.PopupEntity(Loc.GetString("wf-blueprint-assembler-done", ("pattern", Loc.GetString(pattern.Name))), ent, args.User);
    }
}
