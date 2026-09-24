using System.Linq;
using Content.Server.Pinpointer;
using Content.Server.Stack;
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

    private readonly HashSet<Entity<FabricationSiloClientComponent>> _nearby = new();

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
            if (!CanTransmit(ent.Owner, uid, ent.Comp.Kind))
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
            if (proto == null || !IsRecipePart(proto) || ent.Comp.Parts.ContainedEntities.Count >= 250)
                return;

            args.Handled = _containers.Insert(args.Used, ent.Comp.Parts);
        }
        else if (_solutions.TryGetDrainableSolution(args.Used, out var solutionEntity, out var solution))
        {
            foreach (var reagent in solution.Contents.ToArray())
            {
                // The store tracks standard reagent types. Data-bearing reagents must
                // remain in physical containers so their metadata is not discarded.
                if (reagent.Reagent.Data is { Count: > 0 })
                    continue;

                var key = new ProtoId<ReagentPrototype>(reagent.Reagent.Prototype);
                if (!IsRecipeReagent(key) || reagent.Quantity <= FixedPoint2.Zero)
                    continue;

                if (!_solutions.RemoveReagent(solutionEntity.Value, reagent.Reagent, reagent.Quantity))
                    continue;

                ent.Comp.Reagents[key] = ent.Comp.Reagents.GetValueOrDefault(key) + reagent.Quantity;
                args.Handled = true;
            }
        }

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

        var clients = new List<(NetEntity, string)>();
        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, comp.Range, _nearby);
        foreach (var client in _nearby)
        {
            var linked = comp.Kind == FabricationSiloKind.Parts ? client.Comp.PartsSilo : client.Comp.ChemicalSilo;
            if (linked != null && linked != uid || !CanTransmit(uid, client.Owner, comp.Kind))
                continue;

            var status = linked == uid ? " [linked]" : "";
            clients.Add((GetNetEntity(client.Owner),
                $"{Identity.Name(client.Owner, EntityManager)} ({_navMap.GetNearestBeaconString(client.Owner, onlyName: true)}){status}"));
        }

        foreach (var client in comp.Clients)
        {
            if (_nearby.Any(e => e.Owner == client))
                continue;
            clients.Add((GetNetEntity(client), $"{Identity.Name(client, EntityManager)} [linked, out of range]"));
        }

        var stock = new List<(NetEntity?, string)>();
        if (comp.Kind == FabricationSiloKind.Parts && comp.Parts != null)
        {
            foreach (var part in comp.Parts.ContainedEntities)
            {
                var count = TryComp<StackComponent>(part, out var stack) ? stack.Count : 1;
                stock.Add((GetNetEntity(part), $"{MetaData(part).EntityName} ×{count}"));
            }
        }
        else
        {
            foreach (var (reagent, amount) in comp.Reagents.OrderBy(p => p.Key.Id))
                stock.Add((null, $"{_prototypes.Index(reagent).LocalizedName}: {amount}u"));
        }

        _ui.SetUiState(uid, FabricationSiloUiKey.Key,
            new FabricationSiloBuiState(comp.Kind, clients, stock));
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
                QueueDel(part);
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
