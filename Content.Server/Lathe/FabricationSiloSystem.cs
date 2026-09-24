using System.Linq;
using Content.Server.Pinpointer;
using Content.Server.Stack;
using Content.Shared.Popups;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Events;
using Content.Shared.Lathe;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server.Lathe;

/// <summary>
/// Holds actual precursor items and independent reagent quantities for nearby lathes.
/// </summary>
public sealed class FabricationSiloSystem : SharedFabricationSiloSystem
{
    [Dependency] private ContainerSystem _containers = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private NavMapSystem _navMap = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private readonly HashSet<Entity<FabricationSiloClientComponent>> _nearby = new();
    private float _refreshTimer;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _refreshTimer += frameTime;
        if (_refreshTimer < 1f)
            return;
        _refreshTimer = 0f;
        var query = EntityQueryEnumerator<FabricationSiloComponent>();
        while (query.MoveNext(out var uid, out var silo))
            UpdateUi(uid, silo);
    }

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
    }

    private void OnSiloInit(Entity<FabricationSiloComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.Kind == FabricationSiloKind.Parts)
            ent.Comp.Parts = _containers.EnsureContainer<Container>(ent.Owner, "fabrication_parts");
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
            Dirty(uid, client);
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
            Dirty(uid, comp);
            UpdateUi(uid, comp);
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
            if (!CanLink(ent.Owner, uid, ent.Comp.Kind))
                return;

            if (link is { } old && TryComp<FabricationSiloComponent>(old, out var previous))
            {
                previous.Clients.Remove(uid);
                Dirty(old, previous);
                UpdateUi(old, previous);
            }

            link = ent.Owner;
            ent.Comp.Clients.Add(uid);
        }

        Dirty(uid, client);
        Dirty(ent);
        UpdateUi(ent.Owner, ent.Comp);
        RefreshClient(uid);
    }

    private void OnUiOpened(Entity<FabricationSiloComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void OnPowerChanged(EntityUid uid, FabricationSiloComponent comp, ref PowerChangedEvent args)
    {
        RefreshClients(comp);
    }

    private void OnInteractUsing(Entity<FabricationSiloComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Kind == FabricationSiloKind.Parts)
        {
            var proto = MetaData(args.Used).EntityPrototype?.ID;
            if (proto == null || !IsRecipePart(proto))
            {
                _popup.PopupEntity(Loc.GetString("fabrication-silo-part-rejected"), ent.Owner, args.User);
                return;
            }

            if (ent.Comp.Parts.ContainedEntities.Count >= 250)
            {
                args.Handled = true;
                _popup.PopupEntity(Loc.GetString("fabrication-silo-full"), ent.Owner, args.User);
                return;
            }

            args.Handled = _containers.Insert(args.Used, ent.Comp.Parts);
            if (!args.Handled)
                _popup.PopupEntity(Loc.GetString("fabrication-silo-insertion-failed"), ent.Owner, args.User);
        }
        else if (_solutions.TryGetDrainableSolution(args.Used, out var solutionEntity, out var solution))
        {
            // Handle even a rejected transfer so another interaction cannot spill the contents.
            args.Handled = true;
            var transferred = FixedPoint2.Zero;
            var wasEmpty = solution.Volume == FixedPoint2.Zero;
            var rejected = new HashSet<string>();
            foreach (var reagent in solution.Contents.ToArray())
            {
                var key = new ProtoId<ReagentPrototype>(reagent.Reagent.Prototype);
                // The store tracks standard reagent types. Data-bearing reagents must
                // remain in physical containers so their metadata is not discarded.
                if (reagent.Reagent.Data is { Count: > 0 } || !IsRecipeReagent(key))
                {
                    rejected.Add(_prototypes.Index(key).LocalizedName);
                    continue;
                }

                if (reagent.Quantity <= FixedPoint2.Zero)
                    continue;

                if (!_solutions.RemoveReagent(solutionEntity.Value, reagent.Reagent, reagent.Quantity))
                    continue;

                ent.Comp.Reagents[key] = ent.Comp.Reagents.GetValueOrDefault(key) + reagent.Quantity;
                transferred += reagent.Quantity;
            }

            if (transferred > FixedPoint2.Zero)
                _popup.PopupEntity(Loc.GetString("fabrication-silo-transferred", ("amount", transferred.Float())), ent.Owner, args.User);
            if (rejected.Count > 0)
                _popup.PopupEntity(Loc.GetString("fabrication-silo-chemicals-rejected",
                    ("chemicals", string.Join(", ", rejected))), ent.Owner, args.User);
            else if (transferred == FixedPoint2.Zero)
                _popup.PopupEntity(Loc.GetString(wasEmpty ? "fabrication-silo-container-empty" : "fabrication-silo-insertion-failed"), ent.Owner, args.User);
        }
        else if (ent.Comp.Kind == FabricationSiloKind.Chemicals && HasComp<DrainableSolutionComponent>(args.Used))
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("fabrication-silo-container-unavailable"), ent.Owner, args.User);
        }
        else if (ent.Comp.Kind == FabricationSiloKind.Chemicals)
            _popup.PopupEntity(Loc.GetString("fabrication-silo-needs-container"), ent.Owner, args.User);

        if (args.Handled)
        {
            UpdateUi(ent.Owner, ent.Comp);
            RefreshClients(ent.Comp);
        }
    }

    private bool IsRecipePart(EntProtoId prototype)
    {
        foreach (var recipe in _prototypes.EnumeratePrototypes<LatheRecipePrototype>())
            if (recipe.Entities.ContainsKey(prototype))
                return true;
        return false;
    }

    private bool IsRecipeReagent(ProtoId<ReagentPrototype> prototype)
    {
        foreach (var recipe in _prototypes.EnumeratePrototypes<LatheRecipePrototype>())
            if (recipe.Reagents.ContainsKey(prototype))
                return true;
        return false;
    }

    private void OnEjectPart(Entity<FabricationSiloComponent> ent, ref EjectFabricationSiloPartMessage args)
    {
        if (ent.Comp.Kind != FabricationSiloKind.Parts || ent.Comp.Parts == null)
            return;

        var part = GetEntity(args.Part);
        if (!ent.Comp.Parts.Contains(part))
            return;

        if (_containers.Remove(part, ent.Comp.Parts))
        {
            UpdateUi(ent.Owner, ent.Comp);
            RefreshClients(ent.Comp);
        }
    }

    private void RefreshClient(EntityUid uid)
    {
        if (TryComp<LatheComponent>(uid, out var lathe))
            EntityManager.System<LatheSystem>().UpdateUserInterfaceState(uid, lathe);
    }

    private void RefreshClients(FabricationSiloComponent comp)
    {
        foreach (var uid in comp.Clients)
            RefreshClient(uid);
    }

    public void UpdateUi(EntityUid uid, FabricationSiloComponent comp)
    {
        if (!_ui.IsUiOpen(uid, FabricationSiloUiKey.Key))
            return;

        _ui.SetUiState(uid, FabricationSiloUiKey.Key, GetUiState(uid, comp));
    }

    public FabricationSiloBuiState GetUiState(EntityUid uid, FabricationSiloComponent comp)
    {
        var clients = new List<(NetEntity, string, bool, bool)>();
        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, comp.Range, _nearby);
        foreach (var client in _nearby)
        {
            var linked = comp.Kind == FabricationSiloKind.Parts ? client.Comp.PartsSilo : client.Comp.ChemicalSilo;
            if (linked != null && linked != uid || !CanLink(uid, client.Owner, comp.Kind))
                continue;

            clients.Add((GetNetEntity(client.Owner),
                $"{Identity.Name(client.Owner, EntityManager)} ({_navMap.GetNearestBeaconString(client.Owner, onlyName: true)})",
                linked == uid, CanTransmit(uid, client.Owner, comp.Kind)));
        }

        foreach (var client in comp.Clients)
        {
            if (clients.Any(e => e.Item1 == GetNetEntity(client)) || Deleted(client))
                continue;
            clients.Add((GetNetEntity(client), Identity.Name(client, EntityManager), true, CanTransmit(uid, client, comp.Kind)));
        }

        var stock = new List<(NetEntity?, string)>();
        if (comp.Kind == FabricationSiloKind.Parts && comp.Parts != null)
        {
            // Keep the physical items intact, but present identical components as one stock row.
            foreach (var group in comp.Parts.ContainedEntities.Where(part => !TerminatingOrDeleted(part))
                         .GroupBy(part => (MetaData(part).EntityPrototype?.ID, MetaData(part).EntityName)))
            {
                var count = group.Sum(part => TryComp<StackComponent>(part, out var stack) ? stack.Count : 1);
                stock.Add((GetNetEntity(group.First()), $"{group.Key.EntityName} ×{count}"));
            }
        }
        else
        {
            foreach (var (reagent, amount) in comp.Reagents.OrderBy(p => p.Key.Id))
                stock.Add((null, $"{_prototypes.Index(reagent).LocalizedName}: {amount}u"));
        }

        return new FabricationSiloBuiState(comp.Kind, clients, stock);
    }

    public int ConsumeParts(EntityUid client, EntProtoId prototype, int amount)
    {
        if (GetLinkedSilo(client, FabricationSiloKind.Parts) is not { } uid ||
            !TryComp<FabricationSiloComponent>(uid, out var comp) || comp.Parts == null)
            return 0;

        var consumed = 0;
        foreach (var part in comp.Parts.ContainedEntities.ToArray())
        {
            if (MetaData(part).EntityPrototype?.ID != prototype.Id)
                continue;

            var available = TryComp<StackComponent>(part, out var stack) ? stack.Count : 1;
            var take = Math.Min(amount - consumed, available);
            if (take < available)
                _stacks.SetCount(part, available - take);
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
            UpdateUi(uid, comp);
            RefreshClients(comp);
        }
        return consumed;
    }

    public bool ConsumeReagent(EntityUid client, ProtoId<ReagentPrototype> reagent, FixedPoint2 amount)
    {
        if (GetLinkedSilo(client, FabricationSiloKind.Chemicals) is not { } uid ||
            !TryComp<FabricationSiloComponent>(uid, out var comp) ||
            comp.Reagents.GetValueOrDefault(reagent) < amount)
            return false;

        comp.Reagents[reagent] -= amount;
        if (comp.Reagents[reagent] <= FixedPoint2.Zero)
            comp.Reagents.Remove(reagent);
        UpdateUi(uid, comp);
        RefreshClients(comp);
        return true;
    }
}
