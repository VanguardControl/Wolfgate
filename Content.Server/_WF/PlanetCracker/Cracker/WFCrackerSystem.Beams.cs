using System.Numerics;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Camera;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>
/// The cutting beams and the site's own shaking, reconciled every sweep rather than written on edges. No subscriptions.
/// A projector's beam is a networked target the client overlay draws; the surface half of the beam is the same overlay
/// reading the projector's world XY off the anchor, because the projector itself is never in a surface viewer's PVS.
/// </summary>
public sealed partial class WFCrackerSystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;

    /// <summary>How far a mount may drift, in tiles, before the anchor's copy of its position is resent.</summary>
    private const float BeamMoveEpsilon = 0.05f;

    /// <summary>Anchors carrying a beam target this sweep; kept so a dead anchor's stamp is still findable.</summary>
    private readonly HashSet<EntityUid> _beamAnchors = new();

    /// <summary>Next site camera kick per cutting hull; server-only bookkeeping.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextSiteKick = new();

    /// <summary>Beam anchors this pass decided to drop; collected first so the set is not edited mid-walk.</summary>
    private readonly List<EntityUid> _staleBeams = new();

    /// <summary>
    /// Points every projector at one half of the pair and stamps the far end of the beam on each anchor, or takes both
    /// away.
    /// Beams exist only while the cut is actually running: WFProjectorState is a display value the projector's own
    /// power and repair handlers overwrite, so it is never read as "this hull is holding a chunk".
    /// </summary>
    private void ReconcileBeams(Entity<WFPlanetCrackerComponent> ent)
    {
        var cutting = ent.Comp.State == WFCrackState.Cracking && ent.Comp.PendingAbort is null;
        var targeted = TryGetTargetedPair(ent, out var a, out var b);
        var firing = cutting && targeted;

        GetProjectors(ent.Owner, _projectorBuffer);

        // The mount each half's surface beam climbs to: the first one pointed at it, so the far end is stable from one
        // sweep to the next.
        Vector2? aMount = null;
        Vector2? bMount = null;

        for (var i = 0; i < _projectorBuffer.Count; i++)
        {
            var projector = _projectorBuffer[i];

            if (!firing)
            {
                RemComp<WFCrackBeamComponent>(projector.Owner);
                RestoreFacing(projector);
                continue;
            }

            // The list is sorted by grid-local X, so the split is stable from one sweep to the next.
            var first = (i & 1) == 0;
            var anchor = first ? a.Owner : b.Owner;
            var target = GetNetEntity(anchor);

            AimAt(projector, anchor);

            var mount = TransformSystem.GetWorldPosition(projector.Owner);

            if (first)
                aMount ??= mount;
            else
                bMount ??= mount;

            var beam = EnsureComp<WFCrackBeamComponent>(projector.Owner);

            if (beam.Target == target)
                continue;

            beam.Target = target;
            Dirty(projector.Owner, beam);
        }

        if (firing)
        {
            if (aMount is { } aWorld)
                EnsureBeamTarget(a.Owner, aWorld);

            if (bMount is { } bWorld)
                EnsureBeamTarget(b.Owner, bWorld);
        }

        PruneBeamTargets(ent, firing, a.Owner, b.Owner);
    }

    /// <summary>
    /// Swings one projector onto the anchor it is cutting with, remembering the facing the mapper gave it the first
    /// time a cut moves it.
    /// The anchor is several layers below and CE keeps world XY across a stack, so the anchor's own world position is
    /// what the mount points at; the beam overlay projects that same point into the viewer's pass. Rotating an
    /// anchored entity is safe here: the projector's fixture is the inherited square, so no pose of it can straddle a
    /// different set of tiles.
    /// </summary>
    private void AimAt(Entity<WFGravityProjectorComponent> projector, EntityUid anchor)
    {
        var xform = Transform(projector.Owner);

        projector.Comp.PlacedRotation ??= xform.LocalRotation;

        var delta = TransformSystem.GetWorldPosition(anchor) - TransformSystem.GetWorldPosition(xform);

        // Directly on top of its own anchor there is no direction to face, so the mount simply holds what it has.
        if (delta.LengthSquared() <= float.Epsilon)
            return;

        TransformSystem.SetWorldRotation(projector.Owner, Angle.FromWorldVec(delta));
    }

    /// <summary>Hands a projector back the facing it was placed with; a no-op on one no cut has ever moved.</summary>
    private void RestoreFacing(Entity<WFGravityProjectorComponent> projector)
    {
        if (projector.Comp.PlacedRotation is not { } placed)
            return;

        projector.Comp.PlacedRotation = null;
        TransformSystem.SetLocalRotation(projector.Owner, placed);
    }

    /// <summary>
    /// Hands one anchor the world XY of the mount firing at it, which is all a surface viewer needs to draw the same
    /// beam climbing to the hull. Resent only when the mount has actually moved: the sweep runs four times a second.
    /// </summary>
    private void EnsureBeamTarget(EntityUid anchor, Vector2 mount)
    {
        _beamAnchors.Add(anchor);

        var target = EnsureComp<WFCrackBeamTargetComponent>(anchor);

        if ((target.ProjectorWorldPos - mount).LengthSquared() <= BeamMoveEpsilon * BeamMoveEpsilon)
            return;

        target.ProjectorWorldPos = mount;
        Dirty(anchor, target);
    }

    /// <summary>Unstamps an anchor that died, stopped being targeted or whose cut has ended.</summary>
    private void PruneBeamTargets(Entity<WFPlanetCrackerComponent> ent, bool firing, EntityUid a, EntityUid b)
    {
        _staleBeams.Clear();

        foreach (var anchor in _beamAnchors)
        {
            if (TerminatingOrDeleted(anchor) || !HasComp<WFGravityAnchorComponent>(anchor))
            {
                _staleBeams.Add(anchor);
                continue;
            }

            // This hull's own targets keep their beam; everything else below is a candidate for deletion.
            if (firing && (anchor == a || anchor == b))
                continue;

            // An anchor whose hull no longer resolves belongs to nobody's sweep, so leaving it here would light its
            // beam for the rest of the round: a deleted or gibbed hull is exactly what makes TryGetOwner fail while
            // the anchor itself lives on as a separate entity.
            if (!TryGetOwner(anchor, out var owner))
            {
                _staleBeams.Add(anchor);
                continue;
            }

            // Another hull's anchor is that hull's own sweep to reconcile, never this one's.
            if (owner.Owner != ent.Owner)
                continue;

            _staleBeams.Add(anchor);
        }

        foreach (var anchor in _staleBeams)
        {
            _beamAnchors.Remove(anchor);

            if (!TerminatingOrDeleted(anchor))
                RemComp<WFCrackBeamTargetComponent>(anchor);
        }
    }

    /// <summary>
    /// Kicks the cameras of anyone standing near the cut, every SiteKickInterval seconds.
    /// This is the only shaking mechanism with a falloff: the engine's grid shake has no radius at all, which is why
    /// the site is never shaken and the hull is.
    /// </summary>
    private void UpdateSiteEffects(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.State != WFCrackState.Cracking || ent.Comp.PendingAbort is not null)
        {
            _nextSiteKick.Remove(ent.Owner);
            return;
        }

        if (!TryGetTargetedPair(ent, out var a, out var b) || !TryGetCircle(a.Owner, b.Owner, out var centre, out var radius))
            return;

        if (_nextSiteKick.TryGetValue(ent.Owner, out var next) && _timing.CurTime < next)
            return;

        _nextSiteKick[ent.Owner] = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.SiteKickInterval);

        KickCamerasInRange(
            new MapCoordinates(centre, Transform(a.Owner).MapID),
            radius + ent.Comp.SiteKickPadding,
            1f);
    }

    /// <summary>
    /// The explosion system's camera shake, copied rather than called: its own is private.
    /// Public because the chunk extraction throws one hard kick of its own at the same site.
    /// </summary>
    public void KickCamerasInRange(MapCoordinates epicentre, float range, float strength)
    {
        if (range <= 0f)
            return;

        var players = Filter.Empty();
        players.AddInRange(epicentre, range, _playerManager, EntityManager);

        foreach (var player in players.Recipients)
        {
            if (player.AttachedEntity is not { } uid)
                continue;

            var delta = epicentre.Position - TransformSystem.GetWorldPosition(uid);

            if (delta.EqualsApprox(Vector2.Zero))
                delta = new Vector2(0.01f, 0f);

            var distance = delta.Length();

            if (distance > range)
                continue;

            _recoil.KickCamera(uid, -delta.Normalized() * strength * (1f - distance / range));
        }
    }
}
