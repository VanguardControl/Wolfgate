using Content.Server._WG.CargoSales;
using Content.Server._WG.TradeGoods;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Mono.Company;
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
    [Dependency] private WGTradeMarketSystem _market = default!;
    [Dependency] private WGUndergroundWarrantSystem _warrants = default!;

    /// <summary>The company the Underground's people and station carry.</summary>
    public const string Company = "WGUnderground";

    /// <summary>The highest level, at which the station can be struck.</summary>
    public const int Raid = 3;

    private const string SupplyCrate = "CrateTradeContrabandSecureUnderground";
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

    private TimeSpan _lastJob;
    private TimeSpan _cooled;
    private TimeSpan _raidLockedUntil;
    private TimeSpan _nextStep;
    private TimeSpan _nextBearing;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGTradeSaleEvent>(OnTradeSale);
        SubscribeLocalEvent<WGPalletSaleEvent>(OnPalletSale);
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

        var from = _lastJob + TimeSpan.FromSeconds(_cfg.GetCVar(WGUndergroundCVars.HeatGrace));
        if (from < _cooled)
            from = _cooled;
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

        var now = _timing.CurTime;
        if (points > 0)
            _lastJob = now;
        SetHeat(Math.Max(0, Heat + points), now);
    }

    /// <summary>Opens or closes the market. Closed, the Heat cools faster.</summary>
    public void SetClosed(bool closed)
    {
        if (Closed == closed)
            return;

        Closed = closed;
        TellFaction(Loc.GetString(closed ? "wg-underground-market-closed" : "wg-underground-market-open"));
    }

    /// <summary>Pays into the treasury.</summary>
    public void Earn(int thalers)
    {
        if (thalers <= 0)
            return;

        Treasury += thalers;
        Earned += thalers;
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

        if (filter.Count > 0)
            _chat.DispatchFilteredAnnouncement(filter, message, sender: Loc.GetString("wg-underground-sender"), playSound: false, colorOverride: FactionColor);
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
            RaidEnds = now + TimeSpan.FromMinutes(_cfg.GetCVar(WGUndergroundCVars.RaidMinutes));
        else if (level == 2)
            _nextBearing = now;

        Announce(old, level);
        RaiseLocalEvent(new WGUndergroundHeatChangedEvent(old, level));
    }

    private void EndRaid(TimeSpan now)
    {
        RaidEnds = null;
        _raidLockedUntil = now + TimeSpan.FromMinutes(_cfg.GetCVar(WGUndergroundCVars.RaidLockMinutes));
        _lastJob = now;
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

    private void OnPalletSale(ref WGPalletSaleEvent args)
    {
        var crates = 0;
        foreach (var good in args.Goods)
        {
            if (MetaData(good).EntityPrototype?.ID == SupplyCrate)
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

    private void OnMobState(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || args.Origin is not { } killer || !HasComp<WGUndergroundMemberComponent>(killer))
            return;

        if (!HasComp<ActorComponent>(args.Target))
            return;

        var family = WGCompanyFamily.Of(CompOrNull<CompanyComponent>(args.Target)?.CompanyName.Id);
        if (family is "TSF" or "PDV")
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
        _lastJob = TimeSpan.Zero;
        _cooled = TimeSpan.Zero;
        _raidLockedUntil = TimeSpan.Zero;
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
