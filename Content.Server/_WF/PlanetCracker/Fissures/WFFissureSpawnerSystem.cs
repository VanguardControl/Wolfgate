using Content.Server._WF.PlanetCracker.Anchors;
using Content.Server._WF.PlanetCracker.Chunk;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Server.Decals;
using Content.Server.NPC.HTN;
using Content.Server.Parallax;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Fissures;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.NPC.Systems;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Fissures;

/// <summary>
/// Site threats: while an anchor's drill runs it spreads rings of fissures around itself and climbs mobs out of them,
/// and the extraction cut adds one final surge on the disc's perimeter.
/// </summary>
public sealed partial class WFFissureSpawnerSystem : EntitySystem
{
    [Dependency] private AnchorableSystem _anchorable = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCrackerSystem _crackers = default!;
    [Dependency] private WFGravityAnchorSystem _anchors = default!;
    [Dependency] private WFPlanetChunkSystem _chunks = default!;

    /// <summary>Next tick of the 1 Hz ring sweep.</summary>
    private TimeSpan _nextSweep;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // Seven BROADCAST subscriptions, every one by ref because all seven events are [ByRefEvent] record structs
        // (WFAnchorEvents.cs, WFCrackEvents.cs). The one-owner-per-(component, event) rule binds DIRECTED pairs only,
        // so none of these contend with WFGravityAnchorSystem or WFCrackerSystem.
        SubscribeLocalEvent<WFAnchorDrillStartedEvent>(OnDrillStarted);
        SubscribeLocalEvent<WFAnchorDrillFinishedEvent>(OnDrillFinished);
        SubscribeLocalEvent<WFAnchorSwitchedOffEvent>(OnSwitchedOff);
        SubscribeLocalEvent<WFAnchorBrokenEvent>(OnBroken);
        SubscribeLocalEvent<WFAnchorDestroyedEvent>(OnDestroyed);
        SubscribeLocalEvent<WFAnchorPairDissolvedEvent>(OnPairDissolved);

        // WFPlanetChunkSystem.OnCrackCompleted (WFPlanetChunkSystem.cs:68) calls TryExtract synchronously, which stamps
        // the whole disc Tile.Empty; without this ordering constraint the surge would run over a hole.
        SubscribeLocalEvent<WFCrackCompletedEvent>(OnCrackCompleted, before: new[] { typeof(WFPlanetChunkSystem) });

        // The ONE directed pair this system adds. Deliberately not <WFGravityAnchorComponent, ExaminedEvent>, which
        // WFGravityAnchorSystem.cs:59 already owns; a duplicate directed pair crashes the server at start.
        SubscribeLocalEvent<WFFissureSpawnerComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>Arms the spawner: caches the ground, the centre and the faction, and makes ring one due at once.</summary>
    private void OnDrillStarted(ref WFAnchorDrillStartedEvent args)
    {
        if (!TryComp<WFFissureSpawnerComponent>(args.Anchor, out var comp) ||
            !HasComp<WFGravityAnchorComponent>(args.Anchor))
        {
            return;
        }

        var xform = Transform(args.Anchor);

        if (!_anchors.TryGetPlanetGround(xform, out var ground))
            return;

        comp.Ground = ground.Owner;
        comp.Centre = _transform.GetWorldPosition(xform);

        if (TryGetFaction(ground.Owner, out var faction, out var sanctioned))
        {
            comp.Faction = faction;
            comp.Sanctioned = sanctioned;
        }
        else
        {
            // Not an error: the rings still stamp their decals and play the crack sound, they just spawn nothing.
            Log.Debug($"{ToPrettyString(args.Anchor)} armed over {ToPrettyString(ground.Owner)} with no resolvable salvage faction; its fissures will be cosmetic.");
        }

        comp.Armed = true;
        comp.RingsDone = 0;

        // CurTime, never CurTime + interval: seeding the deadline forward puts ring five exactly on DrillEnd, and both
        // sweeps run at 1 Hz with the same phase - WFGravityAnchorSystem.cs:46-47/:292-302 locks the anchor at DrillEnd
        // and this system's own Update disarms on State != Drilling BEFORE it looks at NextRing, so which of the two
        // won would be decided by system registration order. Seeded to now, the five rings land at 0/20/40/60/80% of
        // the drill and a freshly armed anchor is immediately dangerous.
        comp.NextRing = _timing.CurTime;
    }

    /// <summary>The drill finished and the anchor locked; no more rings.</summary>
    private void OnDrillFinished(ref WFAnchorDrillFinishedEvent args)
    {
        Disarm(args.Anchor);
    }

    /// <summary>A locked anchor was switched off.</summary>
    private void OnSwitchedOff(ref WFAnchorSwitchedOffEvent args)
    {
        Disarm(args.Anchor);
    }

    /// <summary>The anchor broke and has to be repaired before it does anything again.</summary>
    private void OnBroken(ref WFAnchorBrokenEvent args)
    {
        Disarm(args.Anchor);
    }

    /// <summary>The anchor entity is terminating.</summary>
    private void OnDestroyed(ref WFAnchorDestroyedEvent args)
    {
        Disarm(args.Anchor);
    }

    /// <summary>The pair stopped being a pair, which cancels any drill on either half.</summary>
    private void OnPairDissolved(ref WFAnchorPairDissolvedEvent args)
    {
        Disarm(args.A);
        Disarm(args.B);
    }

    /// <summary>Examine: how much of the ground this anchor has already split open.</summary>
    private void OnExamined(Entity<WFFissureSpawnerComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Fissures.Count == 0)
            return;

        args.PushMarkup(Loc.GetString("wf-fissure-examine", ("count", ent.Comp.Fissures.Count)));
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<WFFissureSpawnerComponent, WFGravityAnchorComponent>();
        while (query.MoveNext(out var uid, out var spawner, out var anchor))
        {
            if (!spawner.Armed)
                continue;

            // THE CANCEL WITH NO EVENT. Demote (WFGravityAnchorSystem.Pairing.cs:98-110) drops Drilling -> Deployed and
            // zeroes DrillEnd while raising only WFAnchorPairDissolvedEvent, and Dissolve is reached from unanchoring
            // (WFGravityAnchorSystem.cs:121), breakage (:249) and shutdown (:274). Re-checking the state every tick is
            // the only robust stop.
            if (anchor.State != WFAnchorState.Drilling)
            {
                spawner.Armed = false;
                continue;
            }

            // Extraction unanchors and re-anchors a deployed anchor onto the chunk grid (ChunkRide.cs:32); that churn
            // is not a cancel, so the ring schedule is paused rather than torn down.
            if (_anchors.IsRidingChunk(uid))
                continue;

            if (spawner.RingsDone >= spawner.RingCount)
                continue;

            if (_timing.CurTime < spawner.NextRing)
                continue;

            spawner.NextRing = _timing.CurTime + RingInterval(spawner, anchor);
            SpreadRing((uid, spawner), anchor);
            spawner.RingsDone++;
        }
    }

    /// <summary>Stops the ring schedule; decals, pins and live mobs are deliberately left where they are (D19).</summary>
    private void Disarm(EntityUid anchor)
    {
        if (!TryComp<WFFissureSpawnerComponent>(anchor, out var comp))
            return;

        comp.Armed = false;
    }

    /// <summary>
    /// One ring per DrillDuration / RingCount, read live off the anchor every tick.
    /// Never derived from DrillEnd: CompleteDrill rewrites it to CurTime (WFGravityAnchorSystem.Control.cs:20) and
    /// Demote zeroes it (WFGravityAnchorSystem.Pairing.cs:108), so it is not a usable drill-start source.
    /// </summary>
    private static TimeSpan RingInterval(WFFissureSpawnerComponent comp, WFGravityAnchorComponent anchor)
    {
        return anchor.DrillDuration / Math.Max(1, comp.RingCount);
    }

    /// <summary>
    /// The world's salvage faction and whether cracking it is legal, walked off the ground grid.
    /// The three hops are WFDeepVeinSystem.TryGetVeinTable's (WFDeepVeinSystem.cs:110-130): the layer's network, the
    /// network's surface prototype, then the prototype itself. Shared by the arm handler and the surge, and a null
    /// faction on a successful walk is not an error - that world simply has no fissure mobs.
    /// </summary>
    private bool TryGetFaction(EntityUid ground, out ProtoId<SalvageFactionPrototype>? faction, out bool sanctioned)
    {
        faction = null;
        sanctioned = true;

        if (!TryComp<WFPlanetLayerComponent>(ground, out var layer))
            return false;

        if (layer.Network is not { } network || !TryGetEntity(network, out var networkUid))
            return false;

        if (!TryComp<WFPlanetNetworkComponent>(networkUid, out var networkComp))
            return false;

        if (!_proto.TryIndex(networkComp.Surface, out var surface))
            return false;

        sanctioned = surface.Sanctioned;
        faction = sanctioned ? surface.Faction : surface.UnsanctionedFaction ?? surface.Faction;
        return true;
    }
}
