using Content.Shared._WF.Tugs;
using Content.Shared.DoAfter;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Verbs;
using Robust.Shared.Containers;

namespace Content.Server._WF.Tugs;

/// <summary>Strapping a pulled crate to a trolley's bed and taking it off.</summary>
public sealed partial class WFTugSystem
{
    /// <summary>How close the user and the crate must be to the trolley.</summary>
    private const float CargoReach = 2f;

    private void OnTrolleyInit(Entity<WFTugTrolleyComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Cargo = _containers.EnsureContainer<ContainerSlot>(ent, ent.Comp.ContainerId);
    }

    private void AddCargoVerbs(Entity<WFTugTrolleyComponent> ent, EntityUid user, GetVerbsEvent<AlternativeVerb> args)
    {
        // Pulling a crate puts a virtual item in the user's hand; that is not a held item.
        if (args.Using is { } held && !HasComp<VirtualItemComponent>(held))
            return;

        if (ent.Comp.Cargo?.ContainedEntity != null)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wf-trolley-verb-unload"),
                Priority = 2,
                Act = () => StartCargoDoAfter(ent, user, null),
            });
            return;
        }

        if (!TryComp(user, out PullerComponent? puller) || puller.Pulling is not { } crate || !CanLoad(ent, user, crate))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-trolley-verb-load"),
            Priority = 2,
            Act = () => StartCargoDoAfter(ent, user, crate),
        });
    }

    /// <summary>Starts strapping the crate on, or taking the current one off when no crate is given.</summary>
    private void StartCargoDoAfter(Entity<WFTugTrolleyComponent> trolley, EntityUid user, EntityUid? crate)
    {
        if (crate is { } c ? !CanLoad(trolley, user, c) : !CanUnload(trolley, user))
            return;

        DoAfterEvent ev = crate == null ? new WFTrolleyUnloadDoAfterEvent() : new WFTrolleyLoadDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, user, trolley.Comp.CargoDelay, ev, trolley, target: trolley, used: crate)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = false,
            DuplicateCondition = DuplicateConditions.SameEvent | DuplicateConditions.SameTarget,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            Popup(trolley, user, crate == null ? "wf-trolley-unload-start" : "wf-trolley-load-start");
    }

    private void OnLoadDoAfter(Entity<WFTugTrolleyComponent> ent, ref WFTrolleyLoadDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } crate)
            return;

        args.Handled = true;
        if (!CanLoad(ent, args.User, crate))
            return;

        TryLoad(ent, crate);
    }

    private void OnUnloadDoAfter(Entity<WFTugTrolleyComponent> ent, ref WFTrolleyUnloadDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        if (!CanUnload(ent, args.User))
            return;

        // Not the user's own coordinates: a user buckled to a seat is parented to it.
        var offset = _random.NextAngle().ToWorldVec() * 0.8f;
        TryUnload(ent, _transform.GetMoverCoordinates(args.User).Offset(offset));
    }

    /// <summary>Straps a crate to the trolley's bed.</summary>
    public bool TryLoad(Entity<WFTugTrolleyComponent> trolley, EntityUid crate)
    {
        if (trolley.Comp.Cargo is not { ContainedEntity: null } slot || !IsCargo(trolley, crate))
            return false;

        if (TryComp(crate, out PullableComponent? pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(crate, pullable);

        return _containers.Insert(crate, slot);
    }

    /// <summary>Takes the trolley's crate off and sets it down at the given place.</summary>
    public bool TryUnload(Entity<WFTugTrolleyComponent> trolley, Robust.Shared.Map.EntityCoordinates coordinates)
    {
        if (trolley.Comp.Cargo?.ContainedEntity is not { } crate || !_containers.Remove(crate, trolley.Comp.Cargo))
            return false;

        _transform.SetCoordinates(crate, coordinates);
        return true;
    }

    private bool CanLoad(Entity<WFTugTrolleyComponent> trolley, EntityUid user, EntityUid crate)
    {
        return trolley.Comp.Cargo is { ContainedEntity: null } slot &&
               !TerminatingOrDeleted(crate) &&
               IsCargo(trolley, crate) &&
               !IsTrainMoving(trolley) &&
               IsNear(user, trolley) &&
               IsNear(crate, trolley) &&
               _containers.CanInsert(crate, slot);
    }

    private bool CanUnload(Entity<WFTugTrolleyComponent> trolley, EntityUid user)
    {
        return trolley.Comp.Cargo?.ContainedEntity != null && !IsTrainMoving(trolley) && IsNear(user, trolley);
    }

    private bool IsCargo(Entity<WFTugTrolleyComponent> trolley, EntityUid uid)
    {
        return !HasComp<WFTugTrolleyComponent>(uid) &&
               !HasComp<WFTugComponent>(uid) &&
               !Transform(uid).Anchored &&
               _whitelist.IsWhitelistPass(trolley.Comp.CargoWhitelist, uid);
    }

    private bool IsTrainMoving(Entity<WFTugTrolleyComponent> trolley)
    {
        return trolley.Comp.Tug is { } tug && IsMoving(tug);
    }

    private bool IsNear(EntityUid first, EntityUid second)
    {
        var a = _transform.GetMapCoordinates(first);
        var b = _transform.GetMapCoordinates(second);
        return a.MapId == b.MapId && (a.Position - b.Position).Length() <= CargoReach;
    }
}
