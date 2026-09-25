using Content.Server._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Survey;

/// <summary>Server half of the handheld surveyor: a finished scan reveals nearby veins to the user alone.</summary>
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

        SubscribeLocalEvent<WFSurveyorComponent, WFSurveyScanDoAfterEvent>(OnScanDoAfter);
    }

    /// <summary>The completed scan: reveal every vein inside the pulse radius to the user alone.</summary>
    private void OnScanDoAfter(Entity<WFSurveyorComponent> ent, ref WFSurveyScanDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var user = args.User;
        var xform = Transform(user);

        // Deep veins exist only on planet ground and on chunks cut from it.
        if (!_anchors.TryGetPlanetGround(xform, out _) && !(xform.GridUid is { } grid && HasComp<WFPlanetChunkComponent>(grid)))
        {
            _popup.PopupEntity(Loc.GetString("wf-surveyor-not-on-ground"), user, user);
            args.Handled = true;
            return;
        }

        // Spawned server-side; TimedDespawnComponent isn't networked.
        Spawn(ent.Comp.PulseEffect, xform.Coordinates);
        _audio.PlayPvs(ent.Comp.PulseSound, user);

        _veinBuffer.Clear();

        // Uncontained, not StaticSundries: a bodyless anchored vein sits in the dynamic sundries tree.
        _lookup.GetEntitiesInRange(xform.Coordinates, ent.Comp.PulseRadius, _veinBuffer, LookupFlags.Uncontained);

        var surveyed = EnsureComp<WFSurveyedComponent>(user);
        var count = 0;

        foreach (var vein in _veinBuffer)
        {
            // Layers share XY, so hits on other layers are skipped.
            if (Transform(vein.Owner).MapUid != xform.MapUid)
                continue;

            // Counts what the pulse found, not what was new, so a repeat sweep reads the same.
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
