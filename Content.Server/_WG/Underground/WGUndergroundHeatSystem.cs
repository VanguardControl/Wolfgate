using Content.Server._Mono.Store;
using Content.Server._WG.CargoSales;
using Content.Server._WG.TradeGoods;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Radio.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._Mono.Company;
using Content.Shared.FixedPoint;
using Content.Shared._NF.Trade;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.Underground;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Underground;

/// <summary>
/// The Underground's Heat: one faction-wide level that rises with the jobs its members sell, cools when it lies low
/// and opens a raid window on its station at the top. Also keeps the faction's treasury and its round-end tally.
/// </summary>
public sealed partial class WGUndergroundHeatSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CurrencyInjectionSystem _injection = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private WGTradeMarketSystem _market = default!;
    [Dependency] private WGUndergroundWarrantSystem _warrants = default!;

    /// <summary>The company the Underground's people and station carry.</summary>
    public const string Company = "WGUnderground";

    /// <summary>The highest level, at which the station can be struck.</summary>
    public const int Raid = 3;

    private const string SupplyCrate = "CrateTradeContrabandSecureUnderground";
    private static readonly ProtoId<CompanyPrototype> TsfCompany = "TSF";
    private static readonly ProtoId<RadioChannelPrototype> SecurityChannel = "Nfsd";
    private static readonly Color FactionColor = Color.FromHex("#991111");
    private static readonly string[] LevelColors = { "#63c183", "#a3adba", "#d9ad3c", "#e0483a" };
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

    /// <summary>Heat points right now.</summary>
    public double Heat { get; private set; }

    /// <summary>0 QUIET, 1 WATCHED, 2 WANTED, 3 RAID.</summary>
    public int Level { get; private set; }

    /// <summary>Whether the Godfather has closed the market to cool off faster.</summary>
    public bool Closed { get; private set; }

    /// <summary>When the open raid window ends, if one is open.</summary>
    public TimeSpan? RaidEnds { get; private set; }

    /// <summary>Thalers in the faction's treasury.</summary>
    public int Treasury { get; private set; }

    /// <summary>The highest Heat reached this round.</summary>
    public double Peak { get; private set; }

    /// <summary>Jobs sold this round.</summary>
    public int Jobs { get; private set; }

    /// <summary>Members handed in this round.</summary>
    public int Captures { get; set; }

    /// <summary>Thalers of bail paid this round.</summary>
    public int BailPaid { get; set; }

    /// <summary>Thalers the treasury took in this round.</summary>
    public int Earned { get; private set; }

    private TimeSpan _cooled;
    private TimeSpan _raidLockedUntil;
    private TimeSpan _lockdownUntil;
    private TimeSpan _nextStep;
    private TimeSpan _nextBearing;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGTradeSaleEvent>(OnTradeSale);
        SubscribeLocalEvent<WGPalletSaleEvent>(OnPalletSale);
        SubscribeLocalEvent<WGTradeQuoteEvent>(OnQuote);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEnd);
        Treasury = _cfg.GetCVar(WGUndergroundCVars.TreasuryStart);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextStep)
            return;

        _nextStep = now + Step;
        Tick(now);
    }

    /// <summary>Cools the Heat, ends a raid window that has run out and sends the bearing while WANTED.</summary>
    public void Tick(TimeSpan now)
    {
        if (RaidEnds is { } ends)
        {
            if (now >= ends)
                EndRaid(now);
            return;
        }

        if (_lockdownUntil != TimeSpan.Zero && now >= _lockdownUntil)
        {
            _lockdownUntil = TimeSpan.Zero;
            TellFaction(Loc.GetString("wg-underground-lockdown-over"));
        }

        var from = _cooled;
        _cooled = now;
        if (Heat > 0 && now > from)
        {
            var rate = _cfg.GetCVar(WGUndergroundCVars.HeatCooling) * (Closed ? _cfg.GetCVar(WGUndergroundCVars.HeatClosedFactor) : 1f);
            SetHeat(Math.Max(0, Heat - (now - from).TotalMinutes * rate), now);
        }

        if (Level != 2 || now < _nextBearing)
            return;

        _nextBearing = now + TimeSpan.FromMinutes(_cfg.GetCVar(WGUndergroundCVars.BearingMinutes));
        if (Bearing() is { } bearing)
            SecurityRadio(Loc.GetString("wg-underground-intel-bearing", ("bearing", bearing)));
    }

    /// <summary>Adds Heat for a job, or takes some off. Does nothing while a raid window is open.</summary>
    public void Add(double points)
    {
        if (points == 0 || RaidEnds != null)
            return;

        SetHeat(Math.Max(0, Heat + points), _timing.CurTime);
    }

    /// <summary>Whether a raid window is open or its lockdown still runs: the market is shut and the vend locked.</summary>
    public bool Lockdown => RaidEnds != null || _timing.CurTime < _lockdownUntil;

    /// <summary>
    /// What a member's smuggling crate pays now, as a multiple of its price: <c>price_quiet</c> at no Heat, falling to
    /// <c>price_hot</c> just short of RAID, and <c>price_lockdown</c> during a raid window and the lockdown after it.
    /// </summary>
    public float PriceFactor()
    {
        if (Lockdown)
            return Math.Max(0f, _cfg.GetCVar(WGUndergroundCVars.PriceLockdown));

        var raid = _cfg.GetCVar(WGUndergroundCVars.HeatRaid);
        var share = raid <= 0 ? 0 : Math.Clamp(Heat / raid, 0, 1);
        var quiet = _cfg.GetCVar(WGUndergroundCVars.PriceQuiet);
        var hot = _cfg.GetCVar(WGUndergroundCVars.PriceHot);
        return Math.Max(0f, (float) (quiet + (hot - quiet) * share));
    }

    /// <summary>Heat as a whole percentage of the way to RAID.</summary>
    public int Percent()
    {
        var raid = _cfg.GetCVar(WGUndergroundCVars.HeatRaid);
        return raid <= 0 ? 0 : (int) Math.Clamp(Math.Floor(Heat * 100 / raid), 0, 100);
    }

    /// <summary>Opens or closes the market. Closed, the Heat cools faster. False when a lockdown keeps it shut.</summary>
    public bool SetClosed(bool closed)
    {
        if (!closed && Lockdown)
            return false;

        if (Closed == closed)
            return true;

        Closed = closed;
        TellFaction(Loc.GetString(closed ? "wg-underground-market-closed" : "wg-underground-market-open"));
        return true;
    }

    /// <summary>Pays into the treasury.</summary>
    public void Earn(int thalers)
    {
        if (thalers <= 0)
            return;

        Treasury += thalers;
        Earned += thalers;
    }

    /// <summary>Puts back thalers that were taken out and not spent. It is not income, so the round's takings don't move.</summary>
    public void Refund(int thalers)
    {
        if (thalers > 0)
            Treasury += thalers;
    }

    /// <summary>Takes from the treasury, if it holds that much.</summary>
    public bool Spend(int thalers)
    {
        if (thalers < 0 || thalers > Treasury)
            return false;

        Treasury -= thalers;
        return true;
    }

    /// <summary>The line PDAs show under the war level, or null in a round with no Underground station.</summary>
    public string? PdaLine()
    {
        var bases = EntityQueryEnumerator<WGUndergroundBaseComponent>();
        if (!bases.MoveNext(out _, out _))
            return null;

        return Loc.GetString("wg-underground-pda", ("level", Loc.GetString(LevelName(Level))), ("color", LevelColors[Level]));
    }

    /// <summary>The name of a Heat level.</summary>
    public static string LevelName(int level)
    {
        return level switch
        {
            1 => "wg-underground-level-watched",
            2 => "wg-underground-level-wanted",
            Raid => "wg-underground-level-raid",
            _ => "wg-underground-level-quiet",
        };
    }

    /// <summary>Tells everyone flying the Underground's company, and anyone standing on the given grids.</summary>
    public void TellFaction(string message, HashSet<EntityUid>? aboard = null)
    {
        Tell(message, aboard, true);
    }

    /// <summary>Tells only the people standing on the given grids, as the Underground.</summary>
    public void TellAboard(string message, HashSet<EntityUid> aboard)
    {
        Tell(message, aboard, false);
    }

    private void Tell(string message, HashSet<EntityUid>? aboard, bool faction)
    {
        var filter = Recipients(aboard, faction);
        if (filter.Count > 0)
            _chat.DispatchFilteredAnnouncement(filter, message, sender: Loc.GetString("wg-underground-sender"), playSound: false, colorOverride: FactionColor);
    }

    /// <summary>Who a message reaches: players on the given grids and, when asked for, everyone flying the Underground's company.</summary>
    public Filter Recipients(HashSet<EntityUid>? aboard, bool faction)
    {
        var filter = Filter.Empty();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var xform))
        {
            if (faction && CompOrNull<CompanyComponent>(uid)?.CompanyName.Id == Company
                || aboard != null && xform.GridUid is { } grid && aboard.Contains(grid))
            {
                filter.AddPlayer(actor.PlayerSession);
            }
        }

        return filter;
    }

    /// <summary>Speaks on a radio channel as an unnamed source, with no telecoms server needed.</summary>
    public void Radio(string message, ProtoId<RadioChannelPrototype> channel, string speaker)
    {
        var relay = Spawn(null, MapCoordinates.Nullspace);
        _meta.SetEntityName(relay, speaker);
        EnsureComp<TelecomExemptComponent>(relay);
        _radio.SendRadioMessage(relay, message, channel, relay);
        QueueDel(relay);
    }

    private void SecurityRadio(string message)
    {
        Radio(message, SecurityChannel, Loc.GetString("wg-underground-intel-name"));
    }

    private int LevelOf(double heat)
    {
        if (heat >= _cfg.GetCVar(WGUndergroundCVars.HeatRaid))
            return Raid;
        if (heat >= _cfg.GetCVar(WGUndergroundCVars.HeatWanted))
            return 2;
        return heat >= _cfg.GetCVar(WGUndergroundCVars.HeatWatched) ? 1 : 0;
    }

    private void SetHeat(double heat, TimeSpan now)
    {
        // Straight after a raid the Heat can climb back to WANTED but no further.
        if (now < _raidLockedUntil)
            heat = Math.Min(heat, _cfg.GetCVar(WGUndergroundCVars.HeatRaid) - 0.01);

        Heat = heat;
        Peak = Math.Max(Peak, heat);
        var level = LevelOf(heat);
        if (level == Level)
            return;

        var old = Level;
        Level = level;
        if (level == Raid)
            OpenRaid(now);
        else if (level == 2)
            _nextBearing = now;

        Announce(old, level);
        RaiseLocalEvent(new WGUndergroundHeatChangedEvent(old, level));
    }

    /// <summary>Opens a raid window: the market shuts, and the TSF is paid for driving the Underground this far.</summary>
    private void OpenRaid(TimeSpan now)
    {
        RaidEnds = now + TimeSpan.FromMinutes(_cfg.GetCVar(WGUndergroundCVars.RaidMinutes));
        Closed = true;
        TellFaction(Loc.GetString("wg-underground-lockdown-started"));

        var reward = _cfg.GetCVar(WGUndergroundCVars.RaidReward);
        if (reward > 0)
            _injection.InjectCurrency(TsfCompany, new Dictionary<string, FixedPoint2> { [WGUndergroundWarrantSystem.Credits.Id] = reward });
    }

    private void EndRaid(TimeSpan now)
    {
        RaidEnds = null;
        _raidLockedUntil = now + TimeSpan.FromMinutes(_cfg.GetCVar(WGUndergroundCVars.RaidLockMinutes));
        _lockdownUntil = now + TimeSpan.FromMinutes(Math.Max(0f, _cfg.GetCVar(WGUndergroundCVars.LockdownMinutes)));
        _cooled = now;
        Heat = _cfg.GetCVar(WGUndergroundCVars.HeatWatched);
        Level = LevelOf(Heat);
        _chat.DispatchGlobalAnnouncement(Loc.GetString("wg-underground-raid-closed"),
            sender: Loc.GetString("wg-underground-raid-sender"), colorOverride: Color.CornflowerBlue);
        TellFaction(Loc.GetString("wg-underground-told-raid-closed"));
        RaiseLocalEvent(new WGUndergroundHeatChangedEvent(Raid, Level));
    }

    private void Announce(int old, int level)
    {
        var name = Loc.GetString(LevelName(level));
        TellFaction(Loc.GetString(level > old ? "wg-underground-told-rising" : "wg-underground-told-falling", ("level", name)));
        if (level == Raid)
        {
            var place = StationPosition();
            _chat.DispatchGlobalAnnouncement(
                Loc.GetString("wg-underground-raid-open", ("x", (int) (place?.X ?? 0)), ("y", (int) (place?.Y ?? 0)),
                    ("minutes", (int) _cfg.GetCVar(WGUndergroundCVars.RaidMinutes))),
                sender: Loc.GetString("wg-underground-raid-sender"), colorOverride: Color.Crimson);
            return;
        }

        if (level > old)
            SecurityRadio(Loc.GetString(level == 2 ? "wg-underground-intel-wanted" : "wg-underground-intel-watched"));
        else if (level == 0)
            SecurityRadio(Loc.GetString("wg-underground-intel-quiet"));
    }

    private System.Numerics.Vector2? StationPosition()
    {
        var bases = EntityQueryEnumerator<WGUndergroundBaseComponent, TransformComponent>();
        while (bases.MoveNext(out var uid, out _, out var xform))
        {
            // Mid-jump the station is off the map, so this is where it will come out.
            if (TryComp<FTLComponent>(uid, out var ftl) && ftl.State is FTLState.Travelling or FTLState.Arriving)
                return _transform.ToMapCoordinates(ftl.TargetCoordinates).Position;

            return _transform.GetWorldPosition(xform);
        }

        return null;
    }

    /// <summary>The compass bearing of the station from the middle of the sector, in degrees.</summary>
    private int? Bearing()
    {
        if (StationPosition() is not { } place)
            return null;

        var degrees = Math.Atan2(place.X, place.Y) * 180 / Math.PI;
        return (int) Math.Round(degrees < 0 ? degrees + 360 : degrees) % 360;
    }

    private void OnTradeSale(WGTradeSaleEvent args)
    {
        var profile = _market.Profile(args.Grid);
        float points;
        if (profile.ContrabandGoods.Contains(args.Good))
        {
            if (_market.IsHiddenMarket(args.Grid))
                return;
            points = _cfg.GetCVar(WGUndergroundCVars.HeatCrate);
        }
        else if (_market.IsSealedOnly(args.Chute))
        {
            points = _cfg.GetCVar(WGUndergroundCVars.HeatFence);
        }
        else
        {
            return;
        }

        Sold(1, points, args.Seller);
    }

    /// <summary>Quotes a member's smuggling crate by the Heat: above its price while quiet, below it running hot or in a lockdown.</summary>
    private void OnQuote(WGTradeQuoteEvent args)
    {
        if (args.Seller is not { } seller
            || !HasComp<WGUndergroundMemberComponent>(seller)
            || !_market.Profile(args.Grid).ContrabandGoods.Contains(args.Good)
            || _market.IsHiddenMarket(args.Grid))
            return;

        var factor = PriceFactor();
        if (factor > 1f)
        {
            args.Premium *= factor;
            args.PremiumReason = "wg-underground-reason-quiet";
        }
        else if (factor < 1f)
        {
            args.Modifiers.Add(new WGTradeModifier(factor, Lockdown ? "wg-underground-reason-lockdown" : "wg-underground-reason-hot"));
        }
    }

    private void OnPalletSale(ref WGPalletSaleEvent args)
    {
        var crates = 0;
        foreach (var good in args.Goods)
        {
            // Sold anywhere but where it was bound, it pays nothing to speak of and draws no notice.
            if (MetaData(good).EntityPrototype?.ID == SupplyCrate
                && (!TryComp<TradeCrateComponent>(good, out var trade)
                    || trade.DestinationStation == EntityUid.Invalid
                    || _station.GetOwningStation(good) == trade.DestinationStation))
                crates++;
        }

        if (crates > 0)
            Sold(crates, _cfg.GetCVar(WGUndergroundCVars.HeatSupply), args.Seller);
    }

    private void Sold(int jobs, float points, EntityUid? seller)
    {
        Jobs += jobs;
        Earn(jobs * _cfg.GetCVar(WGUndergroundCVars.TreasuryJob));
        Add(jobs * points * HeatShare(seller));
        if (seller is { } worker)
            _warrants.Worked(worker, jobs);
    }

    /// <summary>
    /// How much of a sale's Heat lands. Only the Underground's own people run it up: a courier's sale adds the outsider
    /// share, and the TSF and PDV, who gain from a raid, add none whatever that share is set to.
    /// </summary>
    public float HeatShare(EntityUid? seller)
    {
        if (seller is not { } worker || HasComp<WGUndergroundMemberComponent>(worker))
            return 1f;

        var family = WGCompanyFamily.Of(CompOrNull<CompanyComponent>(worker)?.CompanyName.Id);
        return family is "TSF" or "PDV" ? 0f : Math.Clamp(_cfg.GetCVar(WGUndergroundCVars.HeatOutsider), 0f, 1f);
    }

    /// <summary>Whether this is a TSF or PDV player, whose crit or death at a member's hands adds Heat.</summary>
    public bool IsHunter(EntityUid uid)
    {
        if (!HasComp<ActorComponent>(uid))
            return false;

        var family = WGCompanyFamily.Of(CompOrNull<CompanyComponent>(uid)?.CompanyName.Id);
        return family is "TSF" or "PDV";
    }

    private void OnMobState(MobStateChangedEvent args)
    {
        if (args.Origin is not { } attacker || !HasComp<WGUndergroundMemberComponent>(attacker) || !IsHunter(args.Target))
            return;

        if (args.NewMobState == MobState.Critical)
            Add(_cfg.GetCVar(WGUndergroundCVars.HeatCrit));
        else if (args.NewMobState == MobState.Dead)
            Add(_cfg.GetCVar(WGUndergroundCVars.HeatKill));
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        Reset();
    }

    /// <summary>Puts the Heat, the treasury and the round's tally back to how a round starts.</summary>
    public void Reset()
    {
        Heat = 0;
        Level = 0;
        Closed = false;
        RaidEnds = null;
        Treasury = _cfg.GetCVar(WGUndergroundCVars.TreasuryStart);
        Peak = 0;
        Jobs = 0;
        Captures = 0;
        BailPaid = 0;
        Earned = 0;
        _cooled = _timing.CurTime;
        _raidLockedUntil = TimeSpan.Zero;
        _lockdownUntil = TimeSpan.Zero;
        _nextStep = TimeSpan.Zero;
        _nextBearing = TimeSpan.Zero;
    }

    private void OnRoundEnd(RoundEndTextAppendEvent args)
    {
        if (Jobs == 0 && Captures == 0 && BailPaid == 0)
            return;

        args.AddLine(string.Empty);
        args.AddLine(Loc.GetString("wg-underground-round-end-header"));
        args.AddLine(Loc.GetString("wg-underground-round-end-jobs", ("jobs", Jobs), ("earned", Earned), ("treasury", Treasury)));
        args.AddLine(Loc.GetString("wg-underground-round-end-heat",
            ("level", Loc.GetString(LevelName(LevelOf(Peak)))), ("captures", Captures), ("bail", BailPaid)));
    }
}
