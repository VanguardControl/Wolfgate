using System.Numerics;
using Content.Server._CE.ZLevels.PVS;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Audio;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Repairable;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Anchors;

/// <summary>
/// Server half of the gravity anchor: placement on a planet ground layer, pairing, the unattended drill and the lock.
/// </summary>
public sealed partial class WFGravityAnchorSystem : SharedWFGravityAnchorSystem
{
    [Dependency] private AnchorableSystem _anchorable = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Played once as a drill bites home and the anchor locks.</summary>
    private static readonly SoundSpecifier LockSound = new SoundCollectionSpecifier("MetalThud");

    /// <summary>Accumulator BiomeSystem.ReserveTiles fills; cleared before every call.</summary>
    private readonly List<(Vector2i Index, Tile Tile)> _reservedTiles = new();

    /// <summary>Next tick of the 1 Hz drill sweep.</summary>
    private TimeSpan _nextSweep;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFGravityAnchorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFGravityAnchorComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<WFGravityAnchorComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
        SubscribeLocalEvent<WFGravityAnchorComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<WFGravityAnchorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WFGravityAnchorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFGravityAnchorComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WFGravityAnchorComponent, BreakageEventArgs>(OnBreakage);
        SubscribeLocalEvent<WFGravityAnchorComponent, RepairedEvent>(OnRepaired);
        SubscribeLocalEvent<WFGravityAnchorComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>Pushes the initial appearance and ambience so a mapped or spawned anchor looks right on its first frame.</summary>
    private void OnMapInit(Entity<WFGravityAnchorComponent> ent, ref MapInitEvent args)
    {
        _appearance.SetData(ent.Owner, WFAnchorVisuals.State, ent.Comp.State);
        _appearance.SetData(ent.Owner, WFAnchorVisuals.Damaged, ent.Comp.Damaged);
        _ambient.SetAmbience(ent.Owner, ent.Comp.State == WFAnchorState.Drilling);
    }

    /// <summary>Refuses a wrench-down anywhere but a clear nine-tile square of planet ground.</summary>
    private void OnAnchorAttempt(Entity<WFGravityAnchorComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var xform = Transform(ent);

        if (!TryGetPlanetGround(xform, out var ground))
        {
            // AnchorAttemptEvent carries no reason field, so the popup has to come before the cancel.
            _popup.PopupEntity(Loc.GetString("wf-anchor-not-ground"), ent.Owner, args.User);
            args.Cancel();
            return;
        }

        var origin = _map.TileIndicesFor(ground, xform.Coordinates);

        if (!TryComp<PhysicsComponent>(ent.Owner, out var body) ||
            !FootprintFree(ground, origin, ent.Comp.FootprintRadius, body))
        {
            _popup.PopupEntity(Loc.GetString("wf-anchor-no-room"), ent.Owner, args.User);
            args.Cancel();
        }
    }

    /// <summary>Refuses unwrenching only while the drill is armed; a paired anchor may still be picked up and moved.</summary>
    private void OnUnanchorAttempt(Entity<WFGravityAnchorComponent> ent, ref UnanchorAttemptEvent args)
    {
        if (args.Cancelled || !IsArmed(ent.Comp.State))
            return;

        // An Off anchor is still drilled in but has nothing left to switch off, so it gets its own wording rather than
        // being told to do something it cannot: there is no player-facing re-arm either.
        _popup.PopupEntity(
            Loc.GetString(ent.Comp.State == WFAnchorState.Off ? "wf-anchor-off-unwrench" : "wf-anchor-locked-unwrench"),
            ent.Owner,
            args.User);

        args.Cancel();
    }

    /// <summary>The one anchor/unanchor handler: covers the wrench, explosions and grid destruction alike.</summary>
    private void OnAnchorStateChanged(Entity<WFGravityAnchorComponent> ent, ref AnchorStateChangedEvent args)
    {
        // F5 moves a deployed anchor onto the chunk grid, which is an unanchor and a re-anchor: dissolving the pair here
        // would abort the cut 30 s after a successful extraction, and re-anchoring can never satisfy TryGetPlanetGround.
        if (IsRidingChunk(ent.Owner))
            return;

        if (!args.Anchored)
        {
            RemComp<CEPvsOverrideComponent>(ent.Owner);
            Dissolve(ent);

            if (ent.Comp.State != WFAnchorState.Broken)
                SetState(ent, WFAnchorState.Loose);

            return;
        }

        var xform = Transform(ent);

        // AnchorAttemptEvent fires before the eight-second do-after, and the engine only re-checks the one tile it
        // registers, so the other eight are re-verified here. BeforeAnchoredEvent is not cancellable, so this is post-hoc.
        if (!TryGetPlanetGround(xform, out var ground) || !TryComp<PhysicsComponent>(ent.Owner, out var body))
        {
            _popup.PopupEntity(Loc.GetString("wf-anchor-lost-room"), ent.Owner);
            _transform.Unanchor(ent.Owner, xform);
            return;
        }

        var origin = _map.TileIndicesFor(ground, xform.Coordinates);

        if (!FootprintFree(ground, origin, ent.Comp.FootprintRadius, body, ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("wf-anchor-lost-room"), ent.Owner);
            _transform.Unanchor(ent.Owner, xform);
            return;
        }

        if (ent.Comp.State != WFAnchorState.Broken)
            SetState(ent, WFAnchorState.Deployed);

        ReserveFootprint(ground, origin, ent.Comp.FootprintRadius);
        EnsureComp<CEPvsOverrideComponent>(ent.Owner);

        if (ent.Comp.State == WFAnchorState.Deployed)
            TryPair(ent);
    }

    /// <summary>Both anchor verbs, refused with a disabled entry and a reason rather than by hiding them.</summary>
    private void OnGetVerbs(Entity<WFGravityAnchorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        var user = args.User;

        var drill = new AlternativeVerb
        {
            Text = Loc.GetString("wf-anchor-verb-drill"),
            Priority = 10,
            Act = () => StartDrill(ent, user),
        };

        if (ent.Comp.State == WFAnchorState.Drilling)
        {
            drill.Disabled = true;
            drill.Message = Loc.GetString("wf-anchor-verb-drill-busy");
        }
        else if (ent.Comp.State != WFAnchorState.Paired)
        {
            drill.Disabled = true;
            drill.Message = Loc.GetString("wf-anchor-verb-drill-unpaired");
        }

        args.Verbs.Add(drill);

        var off = new AlternativeVerb
        {
            Text = Loc.GetString("wf-anchor-verb-off"),
            Priority = 9,
            Act = () => SwitchOff(ent, user),
        };

        if (ent.Comp.State != WFAnchorState.Locked)
        {
            off.Disabled = true;
            off.Message = Loc.GetString("wf-anchor-verb-off-not-locked");
        }

        args.Verbs.Add(off);
    }

    /// <summary>Examine: state, the pair geometry, drill progress and the damage line.</summary>
    private void OnExamined(Entity<WFGravityAnchorComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-anchor-examine-state",
            ("state", Loc.GetString(GetStateLocId(ent.Comp.State)))));

        if (TryGetPairDistance(ent, out var distance))
        {
            args.PushMarkup(Loc.GetString("wf-anchor-examine-pair",
                ("distance", MathF.Round(distance, 1)),
                // The sentence reads "tiles across", so the argument is the diameter, not GetCutRadius' radius.
                ("diameter", MathF.Round(GetCutRadius(distance, ent.Comp.CutPadding) * 2f, 1))));
        }
        else if (ent.Comp.State is WFAnchorState.Deployed or WFAnchorState.Loose)
        {
            args.PushMarkup(Loc.GetString("wf-anchor-examine-unpaired",
                ("min", MathF.Round(ent.Comp.MinDistance, 1)),
                ("max", MathF.Round(ent.Comp.MaxDistance, 1))));
        }

        if (ent.Comp.State == WFAnchorState.Drilling && ent.Comp.DrillDuration > TimeSpan.Zero)
        {
            var remaining = ent.Comp.DrillEnd - _timing.CurTime;
            var fraction = 1d - remaining.TotalSeconds / ent.Comp.DrillDuration.TotalSeconds;
            args.PushMarkup(Loc.GetString("wf-anchor-examine-drill",
                ("percent", (int)Math.Clamp(fraction * 100d, 0d, 100d))));
        }

        if (ent.Comp.Damaged)
            args.PushMarkup(Loc.GetString("wf-anchor-examine-damaged"));
    }

    /// <summary>Maintains the networked damage flag. It gates nothing in F3; F4 decides what it pauses.</summary>
    private void OnDamageChanged(Entity<WFGravityAnchorComponent> ent, ref DamageChangedEvent args)
    {
        var damaged = args.Damageable.TotalDamage.Float() >= ent.Comp.BreakDamage * ent.Comp.DamageFraction;

        if (damaged == ent.Comp.Damaged)
            return;

        SetDamaged(ent, damaged);
    }

    /// <summary>The Destructible Breakage threshold: the pair drops and the anchor waits for a repair.</summary>
    private void OnBreakage(Entity<WFGravityAnchorComponent> ent, ref BreakageEventArgs args)
    {
        Dissolve(ent);
        SetState(ent, WFAnchorState.Broken);

        var ev = new WFAnchorBrokenEvent(ent.Owner);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>Repaired: clear both flags and drop back to whatever the anchor's stance says.</summary>
    private void OnRepaired(Entity<WFGravityAnchorComponent> ent, ref RepairedEvent args)
    {
        if (ent.Comp.Damaged)
            SetDamaged(ent, false);

        if (ent.Comp.State != WFAnchorState.Broken)
            return;

        SetState(ent, Transform(ent).Anchored ? WFAnchorState.Deployed : WFAnchorState.Loose);

        if (ent.Comp.State == WFAnchorState.Deployed)
            TryPair(ent);
    }

    /// <summary>Dissolves the pair as the entity terminates, so the survivor is not left pointing at a dead anchor.</summary>
    private void OnShutdown(Entity<WFGravityAnchorComponent> ent, ref ComponentShutdown args)
    {
        Dissolve(ent, false);

        var ev = new WFAnchorDestroyedEvent(ent.Owner);
        RaiseLocalEvent(ref ev);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<WFGravityAnchorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // The damage flag deliberately does not pause the drill; the 50% pause belongs to F4's crack timer.
            if (comp.State != WFAnchorState.Drilling || _timing.CurTime < comp.DrillEnd)
                continue;

            SetState((uid, comp), WFAnchorState.Locked);
            _audio.PlayPvs(LockSound, uid);

            var ev = new WFAnchorDrillFinishedEvent(uid);
            RaiseLocalEvent(ref ev);
        }
    }

    /// <summary>Arms the unattended drill on a paired anchor.</summary>
    private void StartDrill(Entity<WFGravityAnchorComponent> ent, EntityUid user)
    {
        if (ent.Comp.State != WFAnchorState.Paired)
            return;

        ent.Comp.DrillEnd = _timing.CurTime + ent.Comp.DrillDuration;
        SetState(ent, WFAnchorState.Drilling);

        var ev = new WFAnchorDrillStartedEvent(ent.Owner);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>Switches a locked anchor off, unless a later feature vetoes it.</summary>
    private void SwitchOff(Entity<WFGravityAnchorComponent> ent, EntityUid user)
    {
        if (ent.Comp.State != WFAnchorState.Locked)
            return;

        // Broadcast and by value so any number of later systems (F7) may veto and supply a reason.
        var attempt = new WFAnchorSwitchOffAttemptEvent(ent.Owner, user);
        RaiseLocalEvent(attempt);

        if (attempt.Cancelled)
        {
            if (attempt.Reason is { } reason)
                _popup.PopupEntity(Loc.GetString("wf-anchor-verb-off-refused", ("reason", reason)), ent.Owner, user);

            return;
        }

        SetState(ent, WFAnchorState.Off);

        var ev = new WFAnchorSwitchedOffEvent(ent.Owner);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>Resolves the biome-backed ground grid an entity is standing on, or fails on any other grid or map.</summary>
    public bool TryGetPlanetGround(TransformComponent xform, out Entity<MapGridComponent> ground)
    {
        ground = default;

        // The ground layer's map entity IS the grid, so a hull or crate parked on the layer fails here as it must.
        if (xform.GridUid is not { } grid || xform.MapUid != grid)
            return false;

        if (!HasComp<WFPlanetLayerComponent>(grid))
            return false;

        if (!TryComp<CEZMapComponent>(grid, out var zMap) || zMap.Depth != 0)
            return false;

        if (!TryComp<MapGridComponent>(grid, out var mapGrid))
            return false;

        ground = (grid, mapGrid);
        return true;
    }

    /// <summary>Every tile of the square footprint must exist and be free of hard colliders.</summary>
    public bool FootprintFree(
        Entity<MapGridComponent> grid,
        Vector2i origin,
        int radius,
        PhysicsComponent body,
        EntityUid ignore = default)
    {
        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                var idx = origin + new Vector2i(dx, dy);

                // AddToSnapGridCell silently fails on an empty tile, so a hole is as bad as an obstruction.
                if (!_map.TryGetTileRef(grid.Owner, grid.Comp, idx, out var tile) || tile.Tile.IsEmpty)
                    return false;

                // Once the anchor is down it sits in its own centre snap cell, so it has to be excluded from that
                // cell rather than the cell being skipped: AnchorableSystem.TryAnchorEntity is the only other
                // validator of it, and routes that bypass it (admin VV, a future ForceAnchor helper, a map load)
                // would otherwise let a second hard body share the tile unchallenged.
                if (ignore.IsValid() && idx == origin)
                {
                    if (!TileFreeIgnoring(grid, idx, body, ignore))
                        return false;

                    continue;
                }

                if (!_anchorable.TileFree(grid, idx, body.CollisionLayer, body.CollisionMask))
                    return false;
            }
        }

        return true;
    }

    /// <summary>AnchorableSystem.TileFree with one entity - the anchor doing the checking - excluded from the cell.</summary>
    private bool TileFreeIgnoring(Entity<MapGridComponent> grid, Vector2i idx, PhysicsComponent body, EntityUid ignore)
    {
        var enumerator = _map.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, idx);

        while (enumerator.MoveNext(out var other))
        {
            if (other == ignore)
                continue;

            if (!TryComp<PhysicsComponent>(other, out var otherBody) || !otherBody.CanCollide || !otherBody.Hard)
                continue;

            if ((otherBody.CollisionMask & body.CollisionLayer) != 0x0 ||
                (otherBody.CollisionLayer & body.CollisionMask) != 0x0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Pins the biome tiles under the footprint so the ground cannot unload out from under a deployed anchor.</summary>
    private void ReserveFootprint(Entity<MapGridComponent> ground, Vector2i origin, int radius)
    {
        _reservedTiles.Clear();

        var bounds = new Box2(
            origin.X - radius,
            origin.Y - radius,
            origin.X + radius + 1,
            origin.Y + radius + 1);

        // GridUid == MapUid on a ground layer, so the reserved map and the checked grid are one entity. The box stays
        // tile-exact: the tile enumerator floors the minimum and ceils the maximum (SharedMapSystem.Grid.cs:1682-1690),
        // so padding it would pin the ring beyond the footprint - 25 tiles per anchor instead of 9.
        _biome.ReserveTiles(ground.Owner, bounds, _reservedTiles, mapGrid: ground.Comp);
    }

    /// <summary>Locale id for one anchor state.</summary>
    private static string GetStateLocId(WFAnchorState state)
    {
        return state switch
        {
            WFAnchorState.Loose => "wf-anchor-state-loose",
            WFAnchorState.Deployed => "wf-anchor-state-deployed",
            WFAnchorState.Paired => "wf-anchor-state-paired",
            WFAnchorState.Drilling => "wf-anchor-state-drilling",
            WFAnchorState.Locked => "wf-anchor-state-locked",
            WFAnchorState.Off => "wf-anchor-state-off",
            _ => "wf-anchor-state-broken",
        };
    }
}
