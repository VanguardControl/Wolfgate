using System.Linq;
using Content.Shared._WG.StockExchange;
using Content.Shared.DeviceLinking;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Server._WG.StockExchange;

public sealed partial class WGStockSystem
{
    [Dependency] private SharedDeviceLinkSystem _deviceLink = default!;

    private void InitializeSellConsole()
    {
        SubscribeLocalEvent<WGStockSellConsoleComponent, BoundUIOpenedEvent>(OnSellConsoleOpened);
        SubscribeLocalEvent<WGStockSellConsoleComponent, WGStockSellRefreshMessage>(OnSellConsoleRefresh);
        SubscribeLocalEvent<WGStockSellConsoleComponent, WGStockSellAllMessage>(OnSellConsoleAll);
    }

    private void OnSellConsoleOpened(Entity<WGStockSellConsoleComponent> ent, ref BoundUIOpenedEvent args) =>
        SendSellConsole(ent, args.Actor);

    private void OnSellConsoleRefresh(Entity<WGStockSellConsoleComponent> ent, ref WGStockSellRefreshMessage args) =>
        SendSellConsole(ent, args.Actor);

    private void OnSellConsoleAll(Entity<WGStockSellConsoleComponent> ent, ref WGStockSellAllMessage args)
    {
        var owner = ResolveOwner(args.Actor, out _);
        if (!TryGetSellPad(ent, out var pad))
        {
            SendSellConsole(ent, args.Actor, "wg-stock-sell-console-no-pad");
            return;
        }

        var market = EnsureMarket(pad);
        var radius = Comp<WGStockSellSpotComponent>(pad).Radius;
        var sold = false;
        foreach (var entity in PadMaterials(pad, radius))
        {
            if (SellEntity(args.Actor, owner, market, entity, out _))
                sold = true;
        }
        SendSellConsole(ent, args.Actor, sold ? "wg-stock-sell-console-sold" : "wg-stock-sell-console-empty");
    }

    /// <summary>The first sell pad linked to this computer with the mapping tools.</summary>
    private bool TryGetSellPad(EntityUid console, out EntityUid pad)
    {
        pad = EntityUid.Invalid;
        if (!TryComp<DeviceLinkSourceComponent>(console, out var source))
            return false;
        foreach (var sink in _deviceLink.GetLinkedSinks((console, source), WGStockSellConsoleComponent.SourcePort))
        {
            if (!HasComp<WGStockSellSpotComponent>(sink))
                continue;
            pad = sink;
            return true;
        }
        return false;
    }

    /// <summary>Every material entity on or in a container on the sell pad.</summary>
    private List<EntityUid> PadMaterials(EntityUid pad, float radius)
    {
        var nearby = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(Transform(pad).Coordinates, radius, nearby, LookupFlags.Dynamic | LookupFlags.Sundries);
        var found = new HashSet<EntityUid>();
        foreach (var uid in nearby)
        {
            if (uid == pad || TerminatingOrDeleted(uid))
                continue;
            Gather(uid, found);
        }
        return found.Where(uid => !TerminatingOrDeleted(uid) && HasComp<Content.Shared.Materials.MaterialComponent>(uid)).ToList();
    }

    private void Gather(EntityUid uid, HashSet<EntityUid> found)
    {
        if (!found.Add(uid) || !TryComp<ContainerManagerComponent>(uid, out var containers))
            return;
        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
                Gather(contained, found);
        }
    }

    private void SendSellConsole(EntityUid console, EntityUid actor, string? message = null)
    {
        var state = SellConsoleState(console, actor, message);
        _ui.ServerSendUiMessage(console, WGStockSellUiKey.Key, new WGStockSellStateMessage(state), actor);
    }

    private WGStockSellReceiptState SellConsoleState(EntityUid console, EntityUid actor, string? message)
    {
        var state = new WGStockSellReceiptState { Message = message == null ? null : Loc.GetString(message) };
        if (!TryGetSellPad(console, out var pad))
        {
            state.Message ??= Loc.GetString("wg-stock-sell-console-no-pad");
            return state;
        }

        state.Linked = true;
        var owner = ResolveOwner(actor, out _);
        var market = EnsureMarket(pad);
        var quantities = new Dictionary<string, int>();
        foreach (var entity in PadMaterials(pad, Comp<WGStockSellSpotComponent>(pad).Radius))
        {
            if (MaterialId(entity) is not { } material)
                continue;
            var sheets = SheetsIn(entity, EntityManager);
            if (sheets <= 0)
                continue;
            quantities[material] = quantities.GetValueOrDefault(material) + sheets;
        }

        var lines = new List<WGStockSellReceiptLine>();
        foreach (var (material, sheets) in quantities.OrderBy(pair => GoodName(new WGStockGood(pair.Key))))
        {
            var good = new WGStockGood(material);
            if (!IsListed(market.Comp, good))
                continue;
            var remaining = sheets;
            var orders = market.Comp.Orders
                .Where(order => order.Side == WGStockOrderSide.Buy && order.Good == good && order.Quantity > 0 &&
                                (owner == null || order.Account != owner.Account))
                .OrderByDescending(order => order.Price)
                .ThenBy(order => order.Created);
            foreach (var order in orders)
            {
                var take = Math.Min(remaining, order.Quantity);
                if (take <= 0)
                    continue;
                lines.Add(new WGStockSellReceiptLine
                {
                    Buyer = Comp<WGStockAccountComponent>(order.Account).Name,
                    Material = GoodName(good),
                    Quantity = take,
                    Price = order.Price,
                    Total = (long) take * order.Price,
                });
                remaining -= take;
                if (remaining <= 0)
                    break;
            }
        }

        state.Lines = lines.ToArray();
        state.Total = lines.Sum(line => line.Total);
        if (state.Lines.Length == 0 && state.Message == null)
            state.Message = Loc.GetString("wg-stock-sell-console-empty");
        return state;
    }
}
