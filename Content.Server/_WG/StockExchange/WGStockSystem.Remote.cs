using System.Linq;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server.CartridgeLoader;
using Content.Shared._WG.StockExchange;
using Content.Shared.CartridgeLoader;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.StockExchange;

public sealed partial class WGStockSystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;

    public static readonly EntProtoId RemoteCartridge = "WGStockRemoteCartridge";

    private static readonly TimeSpan RemoteRefreshInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextRemoteRefresh;

    private void InitializeRemote()
    {
        SubscribeLocalEvent<WGStockRemoteCartridgeComponent, CartridgeUiReadyEvent>(OnRemoteReady);
        SubscribeLocalEvent<WGPdaProgramsEvent>(OnPdaPrograms);
    }

    private void OnPdaPrograms(ref WGPdaProgramsEvent ev) => ev.Programs.Add(RemoteCartridge);

    private void OnRemoteReady(Entity<WGStockRemoteCartridgeComponent> ent, ref CartridgeUiReadyEvent args) =>
        PushRemote(args.Loader);

    /// <summary>Refreshes open remotes every few seconds.</summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextRemoteRefresh)
            return;
        _nextRemoteRefresh = _timing.CurTime + RemoteRefreshInterval;

        var programs = EntityQueryEnumerator<WGStockRemoteCartridgeComponent, CartridgeComponent>();
        while (programs.MoveNext(out var uid, out _, out var cartridge))
        {
            if (cartridge.LoaderUid is not { } loader || !TryComp<CartridgeLoaderComponent>(loader, out var loaderComp) ||
                loaderComp.ActiveProgram != uid || !_ui.IsUiOpen(loader, loaderComp.UiKey))
                continue;
            PushRemote(loader);
        }
    }

    private void PushRemote(EntityUid loader) =>
        _cartridgeLoader.UpdateCartridgeUiState(loader, RemoteState(RemoteViewer(loader)));

    /// <summary>The person carrying the PDA; a remote on the floor shows markets but no holdings.</summary>
    private EntityUid RemoteViewer(EntityUid loader)
    {
        var parent = Transform(loader).ParentUid;
        return parent.IsValid() ? parent : loader;
    }

    private WGStockRemoteState RemoteState(EntityUid viewer)
    {
        var state = new WGStockRemoteState();
        if (ResolveOwner(viewer, out _) is { } owner)
        {
            if (owner.Kind == WGStockOwnerKind.Company &&
                _players.TryGetSessionByEntity(viewer, out var session) &&
                _companies.StockCompany(session) is { } company)
            {
                RefreshPersistent(owner, company);
                owner = owner with { Balance = company.Balance, Frozen = company.Frozen, Permissions = company.Permissions };
            }

            state.HasOwner = true;
            state.Kind = owner.Kind;
            state.OwnerName = owner.Name;
            state.PersistentCapacity = owner.AccountComp.PersistentCapacity;
            state.PersistentUsed = owner.AccountComp.PersistentMaterials.Values.Sum();
            state.RoundMaterials = owner.AccountComp.RoundMaterials.OrderBy(pair => pair.Key)
                .Select(pair => MaterialState(pair.Key, pair.Value)).ToArray();
            state.PersistentMaterials = owner.AccountComp.PersistentMaterials.OrderBy(pair => pair.Key)
                .Select(pair => MaterialState(pair.Key, pair.Value)).ToArray();
        }

        var markets = new List<WGStockRemoteMarketState>();
        var query = EntityQueryEnumerator<WGStockMarketComponent>();
        while (query.MoveNext(out var uid, out var market))
        {
            var name = MetaData(uid).EntityName;
            markets.Add(new WGStockRemoteMarketState
            {
                Name = string.IsNullOrWhiteSpace(name) ? Loc.GetString("wg-stock-remote-market-name", ("id", uid)) : name,
                Goods = Goods((uid, market)),
            });
        }
        state.Markets = markets.OrderBy(market => market.Name).ToArray();
        return state;
    }
}
