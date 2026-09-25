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

/// <summary>Site threats: a drilling anchor spreads fissure rings that spawn mobs; extraction adds a surge.</summary>
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

        SubscribeLocalEvent<WFAnchorDrillStartedEvent>(OnDrillStarted);
        SubscribeLocalEvent<WFAnchorDrillFinishedEvent>(OnDrillFinished);
        SubscribeLocalEvent<WFAnchorSwitchedOffEvent>(OnSwitchedOff);
        SubscribeLocalEvent<WFAnchorBrokenEvent>(OnBroken);
        SubscribeLocalEvent<WFAnchorDestroyedEvent>(OnDestroyed);
        SubscribeLocalEvent<WFAnchorPairDissolvedEvent>(OnPairDissolved);

        // Before the extraction empties the disc, or the surge would run over a hole.
        SubscribeLocalEvent<WFCrackCompletedEvent>(OnCrackCompleted, before: new[] { typeof(WFPlanetChunkSystem) });

        // On the spawner component: WFGravityAnchorSystem already owns the anchor's ExaminedEvent.
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

        // Now, not now + interval, so the last ring lands before DrillEnd instead of racing the lock.
        comp.NextRing = _timing.CurTime;
    }

    private void OnDrillFinished(ref WFAnchorDrillFinishedEvent args)
    {
        Disarm(args.Anchor);
    }

    private void OnSwitchedOff(ref WFAnchorSwitchedOffEvent args)
    {
        Disarm(args.Anchor);
    }

    private void OnBroken(ref WFAnchorBrokenEvent args)
    {
        Disarm(args.Anchor);
    }

    private void OnDestroyed(ref WFAnchorDestroyedEvent args)
    {
        Disarm(args.Anchor);
    }

    private void OnPairDissolved(ref WFAnchorPairDissolvedEvent args)
    {
        Disarm(args.A);
        Disarm(args.B);
    }

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

            // A drill can end with no event of its own, so the state is re-checked every tick.
            if (anchor.State != WFAnchorState.Drilling)
            {
                spawner.Armed = false;
                continue;
            }

            // Riding onto the chunk isn't a cancel; the schedule just pauses.
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

    /// <summary>Stops the ring schedule; decals, pins and live mobs stay where they are.</summary>
    private void Disarm(EntityUid anchor)
    {
        if (!TryComp<WFFissureSpawnerComponent>(anchor, out var comp))
            return;

        comp.Armed = false;
    }

    /// <summary>One ring per DrillDuration / RingCount; never from DrillEnd, which other paths rewrite.</summary>
    private static TimeSpan RingInterval(WFFissureSpawnerComponent comp, WFGravityAnchorComponent anchor)
    {
        return anchor.DrillDuration / Math.Max(1, comp.RingCount);
    }

    /// <summary>The world's salvage faction and whether cracking it is legal; a null faction spawns no mobs.</summary>
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
