using System.Linq;
using Content.Server.Lathe;
using Content.Server.Pinpointer;
using Content.Server.Stack;
using Content.Shared._WF.Lathe;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Tools.Components;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Lathe;

/// <summary>
/// Stores recipe parts and reagents in fabrication silos and supplies them to linked lathes.
/// </summary>
public sealed partial class FabricationSiloSystem : EntitySystem
{
    [Dependency] private ContainerSystem _containers = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private LatheSystem _lathe = default!;
    [Dependency] private NavMapSystem _navMap = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private readonly HashSet<Entity<FabricationSiloClientComponent>> _nearby = new();
    private readonly HashSet<EntProtoId> _recipeParts = new();
    private readonly HashSet<ProtoId<ReagentPrototype>> _recipeReagents = new();
    private float _refreshTimer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FabricationSiloComponent, ComponentInit>(OnSiloInit);
        SubscribeLocalEvent<FabricationSiloComponent, ComponentShutdown>(OnSiloShutdown);
        SubscribeLocalEvent<FabricationSiloClientComponent, ComponentShutdown>(OnClientShutdown);
        SubscribeLocalEvent<FabricationSiloComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<FabricationSiloComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<FabricationSiloComponent, ToggleFabricationSiloClientMessage>(OnToggleClient);
        SubscribeLocalEvent<FabricationSiloComponent, EjectFabricationSiloPartMessage>(OnEjectPart);
        Subs.BuiEvents<FabricationSiloComponent>(FabricationSiloUiKey.Key,
            subs => subs.Event<BoundUIOpenedEvent>(OnUiOpened));
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        CacheRecipeSupplies();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Machines move and lose power; refresh open windows so range and availability stay current.
        _refreshTimer += frameTime;
        if (_refreshTimer < 1f)
            return;

        _refreshTimer = 0f;
        var query = EntityQueryEnumerator<FabricationSiloComponent>();
        while (query.MoveNext(out var uid, out var silo))
        {
            UpdateUi((uid, silo));
        }
    }

    /// <summary>
    /// Whether any lathe recipe uses this part.
    /// </summary>
    public bool IsRecipePart(EntProtoId prototype)
    {
        return _recipeParts.Contains(prototype);
    }

    /// <summary>
    /// Whether a silo is powered and can reach a machine.
    /// </summary>
    public bool CanTransmit(EntityUid silo, EntityUid client, FabricationSiloKind kind)
    {
        return TryComp<FabricationSiloComponent>(silo, out var store) && CanTransmit((silo, store), client, kind);
    }

    /// <summary>
    /// The silo of this kind that currently supplies a machine, if any.
    /// </summary>
    public EntityUid? GetLinkedSilo(EntityUid client, FabricationSiloKind kind)
    {
        if (!TryComp<FabricationSiloClientComponent>(client, out var link))
            return null;

        var silo = kind == FabricationSiloKind.Parts ? link.PartsSilo : link.ChemicalSilo;
        return silo is { } uid && CanTransmit(uid, client, kind) ? uid : null;
    }

    /// <summary>
    /// Count of a part in a machine's linked parts silo.
    /// </summary>
    public int GetPartAmount(EntityUid client, EntProtoId prototype)
    {
        if (GetLinkedSilo(client, FabricationSiloKind.Parts) is not { } silo ||
            !TryComp<FabricationSiloComponent>(silo, out var comp) ||
            comp.Parts == null)
            return 0;

        var count = 0;
        foreach (var part in comp.Parts.ContainedEntities)
        {
            if (MetaData(part).EntityPrototype?.ID == prototype.Id)
                count += TryComp<StackComponent>(part, out var stack) ? stack.Count : 1;
        }

        return count;
    }

    /// <summary>
    /// Amount of a reagent in a machine's linked chemical silo.
    /// </summary>
    public FixedPoint2 GetReagentAmount(EntityUid client, ProtoId<ReagentPrototype> reagent)
    {
        if (GetLinkedSilo(client, FabricationSiloKind.Chemicals) is not { } silo ||
            !TryComp<FabricationSiloComponent>(silo, out var comp))
            return FixedPoint2.Zero;

        return comp.Reagents.GetValueOrDefault(reagent);
    }

    /// <summary>
    /// Adds the stock of a machine's linked silos to the given totals.
    /// </summary>
    public void CollectStock(EntityUid client,
        Dictionary<EntProtoId, int> parts,
        Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> reagents)
    {
        if (GetLinkedSilo(client, FabricationSiloKind.Parts) is { } partsSilo &&
            TryComp<FabricationSiloComponent>(partsSilo, out var partsStore) &&
            partsStore.Parts != null)
        {
            foreach (var part in partsStore.Parts.ContainedEntities)
            {
                if (MetaData(part).EntityPrototype?.ID is not { } id)
                    continue;

                var proto = new EntProtoId(id);
                parts[proto] = parts.GetValueOrDefault(proto) + (TryComp<StackComponent>(part, out var stack) ? stack.Count : 1);
            }
        }

        if (GetLinkedSilo(client, FabricationSiloKind.Chemicals) is { } chemicalSilo &&
            TryComp<FabricationSiloComponent>(chemicalSilo, out var chemicalStore))
        {
            foreach (var (reagent, amount) in chemicalStore.Reagents)
            {
                reagents[reagent] = reagents.GetValueOrDefault(reagent) + amount;
            }
        }
    }

    /// <summary>
    /// Takes up to the given count of a part from a machine's linked parts silo and returns how many were taken.
    /// </summary>
    public int ConsumeParts(EntityUid client, EntProtoId prototype, int amount)
    {
        if (GetLinkedSilo(client, FabricationSiloKind.Parts) is not { } uid ||
            !TryComp<FabricationSiloComponent>(uid, out var comp) ||
            comp.Parts == null)
            return 0;

        var consumed = 0;
        foreach (var part in comp.Parts.ContainedEntities.ToArray())
        {
            if (MetaData(part).EntityPrototype?.ID != prototype.Id)
                continue;

            var stack = CompOrNull<StackComponent>(part);
            var available = stack?.Count ?? 1;
            var take = Math.Min(amount - consumed, available);
            if (take < available)
            {
                _stacks.SetCount(part, available - take, stack);
            }
            else
            {
                _containers.Remove(part, comp.Parts);
                QueueDel(part);
            }

            consumed += take;
            if (consumed >= amount)
                break;
        }

        if (consumed > 0)
        {
            UpdateUi((uid, comp));
            RefreshClients(comp);
        }

        return consumed;
    }

    /// <summary>
    /// Takes a reagent from a machine's linked chemical silo; fails without taking any if there is not enough.
    /// </summary>
    public bool ConsumeReagent(EntityUid client, ProtoId<ReagentPrototype> reagent, FixedPoint2 amount)
    {
        if (GetLinkedSilo(client, FabricationSiloKind.Chemicals) is not { } uid ||
            !TryComp<FabricationSiloComponent>(uid, out var comp) ||
            comp.Reagents.GetValueOrDefault(reagent) < amount)
            return false;

        comp.Reagents[reagent] -= amount;
        if (comp.Reagents[reagent] <= FixedPoint2.Zero)
            comp.Reagents.Remove(reagent);

        UpdateUi((uid, comp));
        RefreshClients(comp);
        return true;
    }

    /// <summary>
    /// Builds the silo window state.
    /// </summary>
    public FabricationSiloBuiState GetUiState(EntityUid uid, FabricationSiloComponent comp)
    {
        var silo = new Entity<FabricationSiloComponent>(uid, comp);
        var clients = new List<FabricationSiloClientEntry>();
        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, comp.Range, _nearby);
        foreach (var client in _nearby)
        {
            var linked = comp.Kind == FabricationSiloKind.Parts ? client.Comp.PartsSilo : client.Comp.ChemicalSilo;
            if (linked != null && linked != uid || !CanLink(silo, client.Owner))
                continue;

            clients.Add(new FabricationSiloClientEntry(GetNetEntity(client.Owner),
                Identity.Name(client.Owner, EntityManager),
                _navMap.GetNearestBeaconString(client.Owner, onlyName: true),
                linked == uid,
                CanTransmit(silo, client.Owner, comp.Kind)));
        }

        foreach (var client in comp.Clients)
        {
            if (Deleted(client))
                continue;

            var netClient = GetNetEntity(client);
            if (clients.Any(entry => entry.Entity == netClient))
                continue;

            clients.Add(new FabricationSiloClientEntry(netClient,
                Identity.Name(client, EntityManager),
                null,
                true,
                CanTransmit(silo, client, comp.Kind)));
        }

        var stock = new List<FabricationSiloStockEntry>();
        if (comp.Kind == FabricationSiloKind.Parts && comp.Parts != null)
        {
            // Identical parts share one row; the row ejects its first stack.
            foreach (var group in comp.Parts.ContainedEntities.Where(part => !TerminatingOrDeleted(part))
                         .GroupBy(part => (MetaData(part).EntityPrototype?.ID, MetaData(part).EntityName)))
            {
                var count = group.Sum(part => TryComp<StackComponent>(part, out var stack) ? stack.Count : 1);
                stock.Add(new FabricationSiloStockEntry(GetNetEntity(group.First()), group.Key.EntityName, count));
            }
        }
        else
        {
            foreach (var (reagent, amount) in comp.Reagents.OrderBy(pair => pair.Key.Id))
            {
                stock.Add(new FabricationSiloStockEntry(null, _prototypes.Index(reagent).LocalizedName, amount));
            }
        }

        return new FabricationSiloBuiState(comp.Kind, comp.Range, clients, stock);
    }

    private void OnSiloInit(Entity<FabricationSiloComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.Kind == FabricationSiloKind.Parts)
            ent.Comp.Parts = _containers.EnsureContainer<Container>(ent.Owner, FabricationSiloComponent.PartsContainerId);
    }

    private void OnSiloShutdown(Entity<FabricationSiloComponent> ent, ref ComponentShutdown args)
    {
        foreach (var uid in ent.Comp.Clients)
        {
            if (!TryComp<FabricationSiloClientComponent>(uid, out var client))
                continue;

            if (ent.Comp.Kind == FabricationSiloKind.Parts)
                client.PartsSilo = null;
            else
                client.ChemicalSilo = null;

            RefreshClient(uid);
        }
    }

    private void OnClientShutdown(Entity<FabricationSiloClientComponent> ent, ref ComponentShutdown args)
    {
        foreach (var silo in new[] { ent.Comp.PartsSilo, ent.Comp.ChemicalSilo })
        {
            if (silo is not { } uid || !TryComp<FabricationSiloComponent>(uid, out var comp))
                continue;

            comp.Clients.Remove(ent.Owner);
            UpdateUi((uid, comp));
        }
    }

    private void OnToggleClient(Entity<FabricationSiloComponent> ent, ref ToggleFabricationSiloClientMessage args)
    {
        var uid = GetEntity(args.Client);
        if (!TryComp<FabricationSiloClientComponent>(uid, out var client))
            return;

        ref var link = ref (ent.Comp.Kind == FabricationSiloKind.Parts
            ? ref client.PartsSilo
            : ref client.ChemicalSilo);

        if (link == ent.Owner)
        {
            link = null;
            ent.Comp.Clients.Remove(uid);
        }
        else
        {
            if (!CanLink(ent, uid))
                return;

            if (link is { } old && TryComp<FabricationSiloComponent>(old, out var previous))
            {
                previous.Clients.Remove(uid);
                UpdateUi((old, previous));
            }

            link = ent.Owner;
            ent.Comp.Clients.Add(uid);
        }

        UpdateUi(ent);
        RefreshClient(uid);
    }

    private void OnUiOpened(Entity<FabricationSiloComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnPowerChanged(Entity<FabricationSiloComponent> ent, ref PowerChangedEvent args)
    {
        RefreshClients(ent.Comp);
    }

    private void OnInteractUsing(Entity<FabricationSiloComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = ent.Comp.Kind == FabricationSiloKind.Parts
            ? TryInsertPart(ent, args.Used, args.User)
            : TryDepositChemicals(ent, args.Used, args.User);

        if (!args.Handled)
            return;

        UpdateUi(ent);
        RefreshClients(ent.Comp);
    }

    private bool TryInsertPart(Entity<FabricationSiloComponent> ent, EntityUid used, EntityUid user)
    {
        // Tools work the machine itself, even when a recipe uses them.
        if (ent.Comp.Parts == null || HasComp<ToolComponent>(used))
            return false;

        if (MetaData(used).EntityPrototype?.ID is not { } proto || !IsRecipePart(proto))
        {
            _popup.PopupEntity(Loc.GetString("fabrication-silo-part-rejected"), ent, user);
            return false;
        }

        if (ent.Comp.Parts.ContainedEntities.Count >= ent.Comp.MaxParts)
        {
            _popup.PopupEntity(Loc.GetString("fabrication-silo-full"), ent, user);
            return true;
        }

        if (_containers.Insert(used, ent.Comp.Parts))
            return true;

        _popup.PopupEntity(Loc.GetString("fabrication-silo-insertion-failed"), ent, user);
        return false;
    }

    private bool TryDepositChemicals(Entity<FabricationSiloComponent> ent, EntityUid used, EntityUid user)
    {
        if (!_solutions.TryGetDrainableSolution(used, out var solutionEntity, out var solution))
        {
            if (HasComp<DrainableSolutionComponent>(used))
            {
                _popup.PopupEntity(Loc.GetString("fabrication-silo-container-unavailable"), ent, user);
                return true;
            }

            if (!HasComp<ToolComponent>(used))
                _popup.PopupEntity(Loc.GetString("fabrication-silo-needs-container"), ent, user);
            return false;
        }

        // Handled even when nothing moves, so another interaction cannot spill the contents.
        var wasEmpty = solution.Volume == FixedPoint2.Zero;
        var transferred = FixedPoint2.Zero;
        var rejected = new HashSet<string>();
        foreach (var reagent in solution.Contents.ToArray())
        {
            var key = new ProtoId<ReagentPrototype>(reagent.Reagent.Prototype);

            // Data-bearing reagents stay in their container so the data is not lost.
            if (reagent.Reagent.Data is { Count: > 0 } || !_recipeReagents.Contains(key))
            {
                rejected.Add(_prototypes.Index(key).LocalizedName);
                continue;
            }

            var removed = solution.RemoveReagent(reagent);
            if (removed <= FixedPoint2.Zero)
                continue;

            ent.Comp.Reagents[key] = ent.Comp.Reagents.GetValueOrDefault(key) + removed;
            transferred += removed;
        }

        if (transferred > FixedPoint2.Zero)
        {
            _solutions.UpdateChemicals(solutionEntity.Value);
            _popup.PopupEntity(Loc.GetString("fabrication-silo-transferred", ("amount", transferred.Float())), ent, user);
        }

        if (rejected.Count > 0)
        {
            _popup.PopupEntity(Loc.GetString("fabrication-silo-chemicals-rejected",
                ("chemicals", string.Join(", ", rejected))), ent, user);
        }
        else if (transferred == FixedPoint2.Zero)
        {
            _popup.PopupEntity(Loc.GetString(wasEmpty ? "fabrication-silo-container-empty" : "fabrication-silo-insertion-failed"),
                ent,
                user);
        }

        return true;
    }

    private void OnEjectPart(Entity<FabricationSiloComponent> ent, ref EjectFabricationSiloPartMessage args)
    {
        if (ent.Comp.Parts == null)
            return;

        var part = GetEntity(args.Part);
        if (!ent.Comp.Parts.Contains(part) || !_containers.Remove(part, ent.Comp.Parts))
            return;

        UpdateUi(ent);
        RefreshClients(ent.Comp);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<LatheRecipePrototype>())
            CacheRecipeSupplies();
    }

    private void CacheRecipeSupplies()
    {
        _recipeParts.Clear();
        _recipeReagents.Clear();
        foreach (var recipe in _prototypes.EnumeratePrototypes<LatheRecipePrototype>())
        {
            _recipeParts.UnionWith(recipe.Entities.Keys);
            _recipeReagents.UnionWith(recipe.Reagents.Keys);
        }
    }

    private bool CanTransmit(Entity<FabricationSiloComponent> silo, EntityUid client, FabricationSiloKind kind)
    {
        return silo.Comp.Kind == kind && _power.IsPowered(silo.Owner) && CanLink(silo, client);
    }

    private bool CanLink(Entity<FabricationSiloComponent> silo, EntityUid client)
    {
        var grid = _transform.GetGrid(silo.Owner);
        return grid != null &&
               grid == _transform.GetGrid(client) &&
               _transform.InRange(silo.Owner, client, silo.Comp.Range);
    }

    private void RefreshClient(EntityUid uid)
    {
        if (TryComp<LatheComponent>(uid, out var lathe))
            _lathe.UpdateUserInterfaceState(uid, lathe);
    }

    private void RefreshClients(FabricationSiloComponent comp)
    {
        foreach (var uid in comp.Clients)
        {
            RefreshClient(uid);
        }
    }

    private void UpdateUi(Entity<FabricationSiloComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, FabricationSiloUiKey.Key))
            return;

        _ui.SetUiState(ent.Owner, FabricationSiloUiKey.Key, GetUiState(ent, ent.Comp));
    }
}
