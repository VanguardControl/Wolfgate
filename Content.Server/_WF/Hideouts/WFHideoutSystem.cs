using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Administration.Logs;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Hideouts;
using Content.Shared._WF.Hideouts.Blueprint;
using Content.Shared._WF.Outposts;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Hideouts;

/// <summary>
/// Outpost modules: the owner buys a building at the outpost console and is handed a survey stake that lays its prefab
/// onto the outpost, or a kit of packed machines.
/// </summary>
public sealed partial class WFHideoutSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;

    private static readonly EntProtoId StakePrototype = "WFHideoutSurveyStake";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFOutpostComponent, ComponentStartup>(OnOutpostStartup);
        SubscribeLocalEvent<WFHideoutComponent, WFOutpostConsoleStateEvent>(OnConsoleState);
        SubscribeLocalEvent<WFHideoutComponent, WFOutpostCapsEvent>(OnCaps);
        SubscribeLocalEvent<WFHideoutComponent, WFOutpostPodLimitEvent>(OnPodLimit);
        SubscribeLocalEvent<WFHideoutComponent, WFOutpostSettledEvent>(OnSettled);
        SubscribeLocalEvent<WFHideoutComponent, WFOutpostWantsSlotEvent>(OnWantsSlot);
        SubscribeLocalEvent<WFHideoutStakeComponent, AfterInteractEvent>(OnStakeAfterInteract);
        SubscribeLocalEvent<WFHideoutStakeComponent, UseInHandEvent>(OnStakeUseInHand);
        SubscribeLocalEvent<WFHideoutStakeComponent, GetVerbsEvent<AlternativeVerb>>(OnStakeVerbs);
        SubscribeLocalEvent<WFHideoutStakeComponent, ExaminedEvent>(OnStakeExamined);
        Subs.BuiEvents<WFOutpostConsoleComponent>(WFOutpostConsoleUiKey.Key, subs =>
        {
            subs.Event<WFHideoutBuyMessage>(OnBuyMessage);
            subs.Event<WFHideoutRegisterShipMessage>(OnRegisterMessage);
            subs.Event<WFHideoutUnregisterShipMessage>(OnUnregisterMessage);
            subs.Event<WFHideoutRecallShipMessage>(OnRecallMessage);
            subs.Event<WFHideoutInsureMessage>(OnInsureMessage);
            subs.Event<WFHideoutClaimMessage>(OnClaimMessage);
            subs.Event<WFHideoutMoveHomeMessage>(OnMoveHomeMessage);
            subs.Event<WFBlueprintCanvasRequestMessage>(OnCanvasRequest);
            subs.Event<WFBlueprintApplyMessage>(OnBlueprintApply);
        });
        InitializeHangar();
        InitializeHome();
        InitializeStorage();
    }

    private void OnOutpostStartup(Entity<WFOutpostComponent> outpost, ref ComponentStartup args)
    {
        EnsureComp<WFHideoutComponent>(outpost);
    }

    /// <summary>
    /// Sells a module to the outpost's owner, paid from their bank: its stake and kit are left at the console.
    /// <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryBuy(EntityUid user, Entity<WFOutpostComponent> outpost, ProtoId<WFHideoutModulePrototype> id, out string reason)
    {
        if (!_cfg.GetCVar(HideoutCVars.Enabled) || !_outposts.IsOwnerOf(user, outpost.Comp) || !_proto.TryIndex(id, out var module)
            || TerminatingOrDeleted(outpost.Comp.Console))
        {
            reason = "wf-hideout-build-not-owner";
            return false;
        }

        if (module.Company && !HasComp<CompanyComponent>(outpost))
        {
            reason = "wf-hideout-build-company";
            return false;
        }

        if (Stands(outpost, module.Bay))
        {
            reason = "wf-hideout-build-stands";
            return false;
        }

        if (module.Price > 0 && !_bank.TryBankWithdraw(user, module.Price))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        var at = Transform(outpost.Comp.Console).Coordinates;
        foreach (var grant in module.Grants)
        {
            Spawn(grant, at);
        }

        SpawnStake(module, at);
        _popup.PopupEntity(Loc.GetString("wf-hideout-bought", ("module", Loc.GetString(module.Name))), outpost.Comp.Console, user);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{ToPrettyString(user):user} bought {id} for outpost {ToPrettyString(outpost):grid} for {module.Price}");
        reason = string.Empty;
        return true;
    }

    /// <summary>Whether a hangar of a kind already stands on an outpost.</summary>
    private bool Stands(EntityUid outpost, WFHideoutBay bay)
    {
        return bay != WFHideoutBay.None && TryGetHangar(outpost, out _, shipwright: bay == WFHideoutBay.Shipwright);
    }

    private static readonly EntProtoId TradeHubPack = "WFHideoutTradeHubPack";

    /// <summary>Leaves a packed trade hub at a newly founded outpost's console, so its Trade tab can be used from the start.</summary>
    public void LeaveTradeHub(EntityUid outpost)
    {
        if (TryComp<WFOutpostComponent>(outpost, out var comp) && !TerminatingOrDeleted(comp.Console))
            Spawn(TradeHubPack, Transform(comp.Console).Coordinates);
    }

    /// <summary>Leaves the survey stake a module is laid with, set to its prefab; null for a module with none.</summary>
    public EntityUid? SpawnStake(WFHideoutModulePrototype module, EntityCoordinates at)
    {
        if (module.Prefab is not { } prefab)
            return null;

        var stake = Spawn(StakePrototype, at);
        var comp = Comp<WFHideoutStakeComponent>(stake);
        comp.Prefab = prefab;
        comp.Layouts = module.Layouts;
        comp.Bay = module.Bay;
        _metaData.SetEntityName(stake, Loc.GetString("wf-hideout-stake-name", ("module", Loc.GetString(module.Name))));
        return stake;
    }

    /// <summary>What a theme swaps a prefab's parts for as it is laid; null for no theme.</summary>
    public WFPrefabSkin? Skin(WFHideoutThemePrototype? theme)
    {
        return theme == null
            ? null
            : new WFPrefabSkin(theme.Entities.ToDictionary(pair => pair.Key.Id, pair => pair.Value.Id),
                theme.Tiles.ToDictionary(pair => pair.Key.Id, pair => pair.Value.Id));
    }

    /// <summary>How long a stake's mark-out outline stays on the ground.</summary>
    private const float MarkSeconds = 10f;

    /// <summary>Switches a stake between laying its module whole and laying it as a shell.</summary>
    private void OnStakeVerbs(Entity<WFHideoutStakeComponent> stake, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || stake.Comp.Prefab == null)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(stake.Comp.Shell ? "wf-hideout-stake-verb-whole" : "wf-hideout-stake-verb-shell"),
            Act = () =>
            {
                stake.Comp.Shell = !stake.Comp.Shell;
                _popup.PopupEntity(Loc.GetString(stake.Comp.Shell ? "wf-hideout-stake-now-shell" : "wf-hideout-stake-now-whole"), user, user);
            },
        });

        // One verb a layout, the one the stake is set to left out.
        foreach (var layout in stake.Comp.Layouts)
        {
            if (layout.Prefab == stake.Comp.Prefab)
                continue;

            var picked = layout;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString(picked.Name),
                Category = LayoutCategory,
                Act = () =>
                {
                    stake.Comp.Prefab = picked.Prefab;
                    _popup.PopupEntity(Loc.GetString("wf-hideout-stake-now-layout", ("layout", Loc.GetString(picked.Name))), user, user);
                },
            });
        }

        // One verb a theme, the one the stake is set to left out.
        foreach (var theme in _proto.EnumeratePrototypes<WFHideoutThemePrototype>().OrderBy(theme => theme.Order).ThenBy(theme => theme.ID))
        {
            if (theme.ID == (stake.Comp.Theme ?? DefaultTheme))
                continue;

            var picked = theme;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wf-hideout-stake-verb-theme", ("theme", Loc.GetString(picked.Name)), ("price", picked.Price)),
                Category = ThemeCategory,
                Priority = -picked.Order,
                Act = () =>
                {
                    stake.Comp.Theme = picked.ID;
                    _popup.PopupEntity(Loc.GetString("wf-hideout-stake-now-theme", ("theme", Loc.GetString(picked.Name)), ("price", picked.Price)), user, user);
                },
            });
        }
    }

    /// <summary>The theme a stake lays in until it is set to another: the prefab as drawn.</summary>
    public const string DefaultTheme = "WFHideoutThemeColony";

    private static readonly VerbCategory ThemeCategory = new("wf-hideout-stake-verb-themes", null);
    private static readonly VerbCategory LayoutCategory = new("wf-hideout-stake-verb-layouts", null);

    /// <summary>The theme a stake is set to, if it changes anything.</summary>
    private WFHideoutThemePrototype? Theme(WFHideoutStakeComponent stake)
    {
        return stake.Theme != null && _proto.TryIndex<WFHideoutThemePrototype>(stake.Theme, out var theme) ? theme : null;
    }

    private void OnStakeExamined(Entity<WFHideoutStakeComponent> stake, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(stake.Comp.Shell ? "wf-hideout-stake-examine-shell" : "wf-hideout-stake-examine-whole"));
        if (stake.Comp.Layouts.Find(layout => layout.Prefab == stake.Comp.Prefab) is { } current)
            args.PushMarkup(Loc.GetString("wf-hideout-stake-examine-layout", ("layout", Loc.GetString(current.Name))));
        if (Theme(stake.Comp) is { } theme)
            args.PushMarkup(Loc.GetString("wf-hideout-stake-examine-theme", ("theme", Loc.GetString(theme.Name)), ("price", theme.Price)));
    }

    /// <summary>Used in hand, a stake outlines its module on the tile in front of its user, facing the way they face.</summary>
    private void OnStakeUseInHand(Entity<WFHideoutStakeComponent> stake, ref UseInHandEvent args)
    {
        if (args.Handled || stake.Comp.Prefab is not { } prefab)
            return;

        args.Handled = true;
        var user = args.User;
        if (!TryComp<ActorComponent>(user, out var actor) || !_outposts.TryGetOutpost(actor.PlayerSession.UserId, out var outpost))
        {
            _popup.PopupEntity(Loc.GetString("wf-hideout-stake-no-outpost"), user, user);
            return;
        }

        var rotation = _transform.GetWorldRotation(user);
        var facing = rotation.GetCardinalDir();
        var target = _transform.GetMapCoordinates(user).Offset(facing.ToVec());
        var coords = _transform.ToCoordinates(outpost.Comp.Ground, target);
        _popup.PopupEntity(_outposts.TryMarkPrefab(user, outpost, prefab, coords, facing, MarkSeconds, out var reason)
            ? Loc.GetString("wf-hideout-stake-marked", ("seconds", (int) MarkSeconds))
            : Loc.GetString(reason), user, user);
    }

    private void OnStakeAfterInteract(Entity<WFHideoutStakeComponent> stake, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        args.Handled = true;
        if (!TryUseStake(args.User, stake, args.ClickLocation, out var reason))
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
    }

    /// <summary>
    /// Lays a stake's prefab onto its user's outpost at <paramref name="target"/>, extending the way the user faces, and
    /// uses the stake up. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryUseStake(EntityUid user, Entity<WFHideoutStakeComponent> stake, EntityCoordinates target, out string reason)
    {
        if (stake.Comp.Prefab is not { } prefab || !TryComp<ActorComponent>(user, out var actor)
            || !_outposts.TryGetOutpost(actor.PlayerSession.UserId, out var outpost))
        {
            reason = "wf-hideout-stake-no-outpost";
            return false;
        }

        if (Stands(outpost, stake.Comp.Bay))
        {
            reason = "wf-hideout-build-stands";
            return false;
        }

        var facing = _transform.GetWorldRotation(user).GetCardinalDir();
        // A theme's fee is taken first and paid back if the module can't go there.
        var theme = Theme(stake.Comp);
        var fee = theme?.Price ?? 0;
        if (fee > 0 && !_bank.TryBankWithdraw(user, fee))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        var skin = Skin(theme);
        if (!_outposts.TryStampPrefab(user, outpost, prefab, target, facing, out reason, stake.Comp.Shell, skin))
        {
            if (fee > 0)
                _bank.TryBankDeposit(user, fee, tax: false);
            return false;
        }

        // A bay's console takes the cranes laid with it.
        var shipwright = EntityManager.System<Shipwright.WFShipwrightSystem>();
        var consoles = EntityQueryEnumerator<Shipwright.WFShipwrightConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var console, out var consoleComp, out var xform))
        {
            if (xform.GridUid == outpost.Owner)
                shipwright.LinkCranes((console, consoleComp));
        }

        QueueDel(stake);
        _outposts.RefreshConsole(outpost);
        return true;
    }

    /// <summary>A hideout keeps itself: it is saved from the start, with no manifest to file.</summary>
    private void OnWantsSlot(Entity<WFHideoutComponent> outpost, ref WFOutpostWantsSlotEvent args)
    {
        args.Wanted |= _cfg.GetCVar(HideoutCVars.Enabled);
    }

    private void OnCaps(Entity<WFHideoutComponent> outpost, ref WFOutpostCapsEvent args)
    {
        if (!_cfg.GetCVar(HideoutCVars.Enabled))
            return;

        var rules = HideoutRules;
        var company = HasComp<CompanyComponent>(outpost);
        args.Tiles += rules.Tiles + (company ? rules.CompanyTiles : 0);
        args.Entities += rules.Entities + (company ? rules.CompanyEntities : 0);
        args.Span += rules.Span;
    }

    private void OnPodLimit(Entity<WFHideoutComponent> outpost, ref WFOutpostPodLimitEvent args)
    {
        if (!_cfg.GetCVar(HideoutCVars.Enabled))
            return;

        var rules = HideoutRules;
        args.Limit += rules.Pods + (HasComp<CompanyComponent>(outpost) ? rules.CompanyPods : 0);
    }

    private void OnConsoleState(Entity<WFHideoutComponent> outpost, ref WFOutpostConsoleStateEvent args)
    {
        if (!_cfg.GetCVar(HideoutCVars.Enabled) || !TryComp<WFOutpostComponent>(outpost, out var comp))
            return;

        EnsureHome((outpost, outpost.Comp, comp));
        var balance = 0;
        if (_players.TryGetSessionById(comp.Owner, out var session))
            _bank.TryGetBalance(session, out balance);

        var rules = HideoutRules;
        args.State.Hideout = new WFHideoutView(
            balance,
            HasComp<CompanyComponent>(outpost),
            HangarView((outpost, outpost.Comp, comp)),
            TryGetHangar(outpost, out _, shipwright: true),
            StashUsed(outpost),
            rules.StashPoints,
            outpost.Comp.HomeWorld,
            HomeWorlds().Select(s => new WFHideoutWorldView(s.ID, WorldName(s))).ToList(),
            rules.MoveFee);
    }

    private void OnBuyMessage(Entity<WFOutpostConsoleComponent> console, ref WFHideoutBuyMessage args)
    {
        if (!TryOutpost(console, out var outpost))
            return;

        if (!TryBuy(args.Actor, outpost, args.Module, out var reason))
            _popup.PopupEntity(Loc.GetString(reason), args.Actor, args.Actor);
        _outposts.RefreshConsole(outpost);
    }

    private bool TryOutpost(Entity<WFOutpostConsoleComponent> console, out Entity<WFOutpostComponent> outpost)
    {
        outpost = default;
        if (!_cfg.GetCVar(HideoutCVars.Enabled) || console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var comp))
            return false;

        outpost = (grid, comp);
        return true;
    }
}
