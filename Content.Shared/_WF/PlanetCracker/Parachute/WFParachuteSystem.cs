using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._CE.ZLevels.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._WF.PlanetCracker.Parachute;

/// <summary>
/// Parachutes: strap one onto a crate, an object or a person, push them off the ship, and the canopy opens on the way
/// down and holds the fall under the speed a landing hurts at. The pack is left where they land.
/// Shared so the wearer's own client predicts the held fall rather than fighting the server over it.
/// </summary>
public sealed partial class WFParachuteSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Height over the ground under which a canopy that has stopped sinking counts as landed.</summary>
    private const float LandedHeight = 0.05f;

    private readonly List<EntityUid> _landed = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFParachuteComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WFParachuteComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WFParachuteComponent, WFParachuteAttachDoAfterEvent>(OnAttachDoAfter);
        SubscribeLocalEvent<WFParachutedComponent, CEZFallingDamageCalculateEvent>(OnFallDamage);
        SubscribeLocalEvent<WFParachutedComponent, CEZLevelFallMapEvent>(OnFallMap);
    }

    private void OnAfterInteract(Entity<WFParachuteComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        args.Handled = TryStartAttach(ent, args.User, target);
    }

    private void OnUseInHand(Entity<WFParachuteComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartAttach(ent, args.User, args.User);
    }

    private bool TryStartAttach(Entity<WFParachuteComponent> ent, EntityUid user, EntityUid target)
    {
        // Anything the z-levels can drop can wear one; a wall or a floor tile cannot fall in the first place.
        if (!TryComp<CEZPhysicsComponent>(target, out var zPhysics) || !zPhysics.Fallable || Transform(target).Anchored)
        {
            _popup.PopupClient(Loc.GetString("wf-parachute-cannot-attach"), target, user);
            return false;
        }

        if (HasComp<WFParachutedComponent>(target))
        {
            _popup.PopupClient(Loc.GetString("wf-parachute-already"), target, user);
            return false;
        }

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.AttachTime,
            new WFParachuteAttachDoAfterEvent(), ent, target: target, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnAttachDoAfter(Entity<WFParachuteComponent> ent, ref WFParachuteAttachDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target || HasComp<WFParachutedComponent>(target))
            return;

        args.Handled = true;

        EnsureComp<WFParachutedComponent>(target);
        _audio.PlayPredicted(ent.Comp.AttachSound, target, args.User);
        _popup.PopupClient(Loc.GetString(target == args.User ? "wf-parachute-attached-self" : "wf-parachute-attached",
            ("target", target)), target, args.User);

        PredictedQueueDel(ent.Owner);
    }

    /// <summary>A held fall never reaches the hurting speed, but anything that still lands hard under canopy is spared.</summary>
    private void OnFallDamage(Entity<WFParachutedComponent> ent, ref CEZFallingDamageCalculateEvent args)
    {
        if (args.Fallen != ent.Owner || !ent.Comp.Deployed)
            return;

        args.DamageMultiplier = 0f;
        args.StunMultiplier = 0f;
    }

    /// <summary>
    /// The canopy opens on falling through a level, never on speed alone: a step down off a ledge is not a jump, and
    /// would otherwise open the pack and hand it straight back.
    /// </summary>
    private void OnFallMap(Entity<WFParachutedComponent> ent, ref CEZLevelFallMapEvent args)
    {
        if (ent.Comp.Deployed)
            return;

        ent.Comp.Deployed = true;
        Dirty(ent);

        if (_net.IsServer)
            _audio.PlayPvs(ent.Comp.DeploySound, ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _landed.Clear();

        var query = EntityQueryEnumerator<WFParachutedComponent, CEZPhysicsComponent>();

        while (query.MoveNext(out var uid, out var chute, out var zPhysics))
        {
            if (!chute.Deployed)
                continue;

            if (zPhysics.Velocity < -chute.FallSpeed)
                _zLevels.SetZVelocity((uid, zPhysics), -chute.FallSpeed);

            if (zPhysics.Velocity >= 0f && zPhysics.LocalPosition - zPhysics.CachedGroundHeight <= LandedHeight)
                _landed.Add(uid);
        }

        // The pack is the server's to hand back; the client only stops predicting the canopy.
        foreach (var uid in _landed)
        {
            if (!TryComp<WFParachutedComponent>(uid, out var chute))
                continue;

            if (_net.IsServer)
            {
                Spawn(chute.Pack, Transform(uid).Coordinates);
                RemComp<WFParachutedComponent>(uid);
            }
            else
            {
                chute.Deployed = false;
            }
        }
    }
}
