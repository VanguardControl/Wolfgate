using Content.Server._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Survey;

/// <summary>
/// Server half of the handheld surveyor: the scan DoAfter's completion, which is the one place a deep vein is ever
/// revealed. Every reveal write is server-authored and lands on the user's own <see cref="WFSurveyedComponent"/>, so
/// one player's pulse never shows another player anything.
/// </summary>
public sealed partial class WFSurveyorSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFGravityAnchorSystem _anchors = default!;

    /// <summary>Veins found by one pulse, reused so a scan allocates nothing.</summary>
    private readonly HashSet<Entity<WFDeepVeinComponent>> _veinBuffer = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // A DIFFERENT pair from the shared system's <WFSurveyorComponent, UseInHandEvent>, which only starts the
        // DoAfter. It fires on cancellation too, so nothing else is needed to end a scan.
        SubscribeLocalEvent<WFSurveyorComponent, WFSurveyScanDoAfterEvent>(OnScanDoAfter);
    }

    /// <summary>
    /// The completed scan: reveal every vein inside the pulse radius to the user alone.
    /// The surveyor has no scanning face in F2 (plan D-N), so this system takes no appearance dependency, adds no
    /// second subscription and runs no per-frame sweep.
    /// </summary>
    private void OnScanDoAfter(Entity<WFSurveyorComponent> ent, ref WFSurveyScanDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var user = args.User;
        var xform = Transform(user);

        // The canonical ground-layer test, shared with the gravity anchors: deep veins only exist on a planet's depth 0
        // biome grid, so a scan anywhere else has nothing to find and says so rather than reading empty. The one other
        // place is the disc cut out of that ground: its veins rode up anchored to the chunk, wherever it now hangs.
        if (!_anchors.TryGetPlanetGround(xform, out _) && !(xform.GridUid is { } grid && HasComp<WFPlanetChunkComponent>(grid)))
        {
            _popup.PopupEntity(Loc.GetString("wf-surveyor-not-on-ground"), user, user);
            args.Handled = true;
            return;
        }

        // Server-side only: TimedDespawnComponent is explicitly not networked
        // (RobustToolbox/Robust.Shared/Spawners/TimedDespawnComponent.cs:10), so the effect is spawned where it is seen.
        Spawn(ent.Comp.PulseEffect, xform.Coordinates);
        _audio.PlayPvs(ent.Comp.PulseSound, user);

        _veinBuffer.Clear();

        // THE FLAG IS Uncontained, NOT StaticSundries. An anchored vein carries no PhysicsComponent, and the broadphase
        // picks its tree with `staticBody: body?.BodyType == BodyType.Static`
        // (RobustToolbox/Robust.Shared/GameObjects/Systems/EntityLookupSystem.cs:648-657) then
        // `staticBody ? StaticSundriesTree : SundriesTree` (:491-496) - a null body is not Static, so a bodyless entity
        // rides the DYNAMIC SundriesTree no matter how it is anchored. StaticSundries would still find it, but only
        // through its Sundries bit (dispatch at EntityLookupSystem.ComponentQueries.cs:165-174); the Static bit is dead
        // weight and the name is actively misleading. Uncontained is also the flag the chunk ride-up query uses, so the
        // surveyor and the extraction agree by construction.
        _lookup.GetEntitiesInRange(xform.Coordinates, ent.Comp.PulseRadius, _veinBuffer, LookupFlags.Uncontained);

        var surveyed = EnsureComp<WFSurveyedComponent>(user);
        var count = 0;

        foreach (var vein in _veinBuffer)
        {
            // A planet network stacks its layers at the same XY, so a same-radius hit on the air layer above is a real
            // possibility; the scan is the ground the scanner is standing on and nothing else.
            if (Transform(vein.Owner).MapUid != xform.MapUid)
                continue;

            // The popup counts what the pulse found, not what was new: a second sweep of ground already walked should
            // read the same number, not zero.
            surveyed.Revealed.Add(GetNetEntity(vein.Owner));
            count++;
        }

        surveyed.LastPulse = GetNetCoordinates(xform.Coordinates);
        surveyed.LastPulseRadius = ent.Comp.PulseRadius;
        surveyed.PulseFadeEnd = _timing.CurTime + surveyed.PulseFade;
        Dirty(user, surveyed);

        _popup.PopupEntity(
            count > 0
                ? Loc.GetString("wf-surveyor-found", ("count", count))
                : Loc.GetString("wf-surveyor-found-none"),
            user,
            user);

        args.Handled = true;
    }
}
