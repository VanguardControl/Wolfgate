using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared._Common.Consent;
using Content.Shared._WG.MentorCritters;
using Content.Shared._WG.MentorObserve;
using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Throwing;
using Content.Shared.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.MentorCritters;

/// <summary>
/// Mentor critter bodies (feature plan 4.6): one hand for tiny and small things and simple use, no machine or console
/// windows (D9), riding in the asker's pocket or on their head with their consent (D8), talking to the asker alone
/// unless speaking aloud (D7), pointing things out, and coming back when lost.
/// </summary>
public sealed class WGMentorCritterSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedConsentSystem _consent = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public static readonly ProtoId<ConsentTogglePrototype> RideConsent = "WGMentorCritterRide";
    public static readonly ProtoId<ItemSizePrototype> PocketSize = "Small";

    private static readonly string[] Pockets = { "pocket1", "pocket2" };
    private const string HeadSlot = "head";
    private static readonly TimeSpan ClimbTime = TimeSpan.FromSeconds(1.5);

    /// <summary>How far a critter may stray from the asker, or onto another grid, before it is brought back.</summary>
    private const float LostRange = 30f;

    private TimeSpan _nextCheck;

    /// <summary>Raised with the asker's body when the critter climbs on or off them.</summary>
    public event Action<EntityUid>? RidingChanged;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMentorCritterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGMentorCritterComponent, PickupAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<WGMentorCritterComponent, GettingPickedUpAttemptEvent>(OnGettingPickedUp);
        SubscribeLocalEvent<WGMentorCritterComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<WGMentorCritterComponent, UserOpenActivatableUIAttemptEvent>(OnOpenUiAttempt);
        SubscribeLocalEvent<WGMentorCritterComponent, ThrowAttemptEvent>(OnThrowAttempt);
        SubscribeLocalEvent<WGMentorCritterComponent, BeingUnequippedAttemptEvent>(OnUnequipAttempt);
        SubscribeLocalEvent<WGMentorCritterComponent, CheckTargetedSpeechEvent>(OnCheckSpeech);
        SubscribeLocalEvent<WGMentorCritterComponent, WGMentorCritterPocketActionEvent>((uid, comp, args) => Climb(uid, comp, args, null));
        SubscribeLocalEvent<WGMentorCritterComponent, WGMentorCritterHeadActionEvent>((uid, comp, args) => Climb(uid, comp, args, HeadSlot));
        SubscribeLocalEvent<WGMentorCritterComponent, WGMentorCritterDownActionEvent>(OnDown);
        SubscribeLocalEvent<WGMentorCritterComponent, WGMentorCritterAloudActionEvent>(OnAloud);
        SubscribeLocalEvent<WGMentorCritterComponent, WGMentorCritterPingActionEvent>(OnPing);
        SubscribeLocalEvent<WGMentorCritterComponent, WGMentorCritterClimbDoAfterEvent>(OnClimbDone);
    }

    #region Spawning

    /// <summary>Spawns a critter next to the asker, named for its animal.</summary>
    public EntityUid Spawn(WGMentorCritterPrototype proto, EntityUid asker, int ticket)
    {
        var critter = Spawn(proto.Entity, Transform(asker).Coordinates);
        var comp = EnsureComp<WGMentorCritterComponent>(critter);
        comp.Asker = asker;
        comp.Ticket = ticket;
        comp.Animal = Loc.GetString(proto.Animal);
        Dirty(critter, comp);

        _meta.SetEntityName(critter, Loc.GetString("wg-mentor-critter-name", ("animal", comp.Animal)));
        _meta.SetEntityDescription(critter, Loc.GetString("wg-mentor-critter-desc", ("animal", comp.Animal)));
        return critter;
    }

    /// <summary>Takes the critter out of the asker's pocket or off their head, onto the floor beside them.</summary>
    public void TakeOut(EntityUid critter)
    {
        if (!TryComp<WGMentorCritterComponent>(critter, out var comp) || comp.Asker is not { } asker
            || !_inventory.TryGetContainingSlot(critter, out var slot))
        {
            return;
        }

        comp.Moving = true;
        _inventory.TryUnequip(asker, asker, slot.Name, silent: true, force: true);
        comp.Moving = false;
        _transform.DropNextTo(critter, asker);
        RidingChanged?.Invoke(asker);
    }

    /// <summary>The critter goes: out of any slot, deleted, and the asker told.</summary>
    public void Despawn(EntityUid critter, EntityUid asker)
    {
        TakeOut(critter);
        QueueDel(critter);
        if (Exists(asker))
            _popup.PopupEntity(Loc.GetString("wg-mentor-critter-gone"), asker, asker);
    }

    /// <summary>Whether the critter rides in a pocket or on a head.</summary>
    public bool IsRiding(EntityUid critter)
    {
        return _container.IsEntityInContainer(critter);
    }

    private void OnMapInit(EntityUid uid, WGMentorCritterComponent comp, MapInitEvent args)
    {
        _hands.AddHand(uid, "critter", HandLocation.Middle);

        if (TryComp<ItemComponent>(uid, out var item)
            && _item.GetItemSizeWeight(item.Size) <= _item.GetItemSizeWeight(PocketSize))
        {
            _actions.AddAction(uid, "WGActionMentorCritterPocket");
        }

        if (TryComp<ClothingComponent>(uid, out var clothing) && (clothing.Slots & SlotFlags.HEAD) != 0)
            _actions.AddAction(uid, "WGActionMentorCritterHead");

        _actions.AddAction(uid, "WGActionMentorCritterDown");
        _actions.AddAction(uid, "WGActionMentorCritterAloud");
        _actions.AddAction(uid, "WGActionMentorCritterPing");
    }

    #endregion

    #region Limits

    private void OnPickupAttempt(EntityUid uid, WGMentorCritterComponent comp, PickupAttemptEvent args)
    {
        // Tiny and small things only, and nothing from inside a pocket.
        if (IsRiding(uid)
            || !TryComp<ItemComponent>(args.Item, out var item)
            || _item.GetItemSizeWeight(item.Size) > _item.GetItemSizeWeight(PocketSize))
        {
            args.Cancel();
        }
    }

    private void OnGettingPickedUp(EntityUid uid, WGMentorCritterComponent comp, GettingPickedUpAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnInteractionAttempt(EntityUid uid, WGMentorCritterComponent comp, ref InteractionAttemptEvent args)
    {
        if (IsRiding(uid))
            args.Cancelled = true;
    }

    private void OnOpenUiAttempt(EntityUid uid, WGMentorCritterComponent comp, UserOpenActivatableUIAttemptEvent args)
    {
        args.Cancel();
        _popup.PopupEntity(Loc.GetString("wg-mentor-critter-console"), uid, uid);
    }

    private void OnThrowAttempt(EntityUid uid, WGMentorCritterComponent comp, ThrowAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnUnequipAttempt(EntityUid uid, WGMentorCritterComponent comp, BeingUnequippedAttemptEvent args)
    {
        // Only the asker, the critter itself (getting down) and this system take it out.
        if (comp.Moving || args.Unequipee == comp.Asker || args.Unequipee == uid)
            return;

        args.Reason = "wg-mentor-critter-not-yours";
        args.Cancel();
    }

    /// <summary>D7: the mentor voice reaches the asker alone (and ghosts, which hear direct speech), unless speaking aloud.</summary>
    private void OnCheckSpeech(EntityUid uid, WGMentorCritterComponent comp, CheckTargetedSpeechEvent args)
    {
        if (comp.Aloud || comp.Asker is not { } asker)
            return;

        args.Targets.Add(uid);
        args.Targets.Add(asker);
    }

    #endregion

    #region Actions

    private void Climb(EntityUid uid, WGMentorCritterComponent comp, InstantActionEvent args, string? slot)
    {
        if (args.Handled || comp.Asker is not { } asker || IsRiding(uid))
            return;

        args.Handled = true;
        if (!_consent.HasConsent(asker, RideConsent))
        {
            _popup.PopupEntity(Loc.GetString("wg-mentor-critter-no-consent"), uid, uid);
            return;
        }

        slot ??= FreePocket(asker);
        if (slot == null || _inventory.TryGetSlotEntity(asker, slot, out _))
        {
            _popup.PopupEntity(Loc.GetString("wg-mentor-critter-slot-full"), uid, uid);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, uid, ClimbTime, new WGMentorCritterClimbDoAfterEvent { Slot = slot }, uid, asker)
        {
            BreakOnMove = true,
            DistanceThreshold = 1.5f,
            BlockDuplicate = true,
        };
        _doAfter.TryStartDoAfter(doAfter);
    }

    /// <summary>An empty pocket of a worn jumpsuit, or null.</summary>
    private string? FreePocket(EntityUid asker)
    {
        if (!_inventory.TryGetSlotEntity(asker, "jumpsuit", out _))
            return null;

        foreach (var pocket in Pockets)
        {
            if (_inventory.HasSlot(asker, pocket) && !_inventory.TryGetSlotEntity(asker, pocket, out _))
                return pocket;
        }

        return null;
    }

    private void OnClimbDone(EntityUid uid, WGMentorCritterComponent comp, WGMentorCritterClimbDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || comp.Asker is not { } asker || args.Target != asker)
            return;

        args.Handled = true;
        Ride(uid, args.Slot);
    }

    /// <summary>Puts the critter into one of the asker's slots, if it is empty. False when it couldn't.</summary>
    public bool Ride(EntityUid critter, string slot)
    {
        if (!TryComp<WGMentorCritterComponent>(critter, out var comp) || comp.Asker is not { } asker)
            return false;

        if (_inventory.TryGetSlotEntity(asker, slot, out _))
        {
            _popup.PopupEntity(Loc.GetString("wg-mentor-critter-slot-full"), critter, critter);
            return false;
        }

        comp.Moving = true;
        var equipped = _inventory.TryEquip(critter, asker, critter, slot, silent: true, force: true);
        comp.Moving = false;
        RidingChanged?.Invoke(asker);
        return equipped;
    }

    private void OnDown(EntityUid uid, WGMentorCritterComponent comp, WGMentorCritterDownActionEvent args)
    {
        if (args.Handled || !IsRiding(uid))
            return;

        args.Handled = true;
        TakeOut(uid);
    }

    private void OnAloud(EntityUid uid, WGMentorCritterComponent comp, WGMentorCritterAloudActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        comp.Aloud = !comp.Aloud;
        Dirty(uid, comp);
        _actions.SetToggled(args.Action.Owner, comp.Aloud);

        _popup.PopupEntity(Loc.GetString(comp.Aloud ? "wg-mentor-critter-aloud-on" : "wg-mentor-critter-aloud-off"), uid, uid);
    }

    private void OnPing(EntityUid uid, WGMentorCritterComponent comp, WGMentorCritterPingActionEvent args)
    {
        if (args.Handled || comp.Asker is not { } asker || !TryComp<ActorComponent>(asker, out var actor))
            return;

        args.Handled = true;
        RaiseNetworkEvent(new WGMentorPingEvent { Coordinates = GetNetCoordinates(args.Target) }, actor.PlayerSession);
    }

    #endregion

    /// <summary>Brings back a critter that ended up in space, a container, another grid or far away.</summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + TimeSpan.FromSeconds(2);
        var query = EntityQueryEnumerator<WGMentorCritterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Asker is not { } asker || !Exists(asker))
                continue;

            if (_inventory.TryGetContainingSlot(uid, out _) && Transform(uid).ParentUid == asker)
                continue;

            var askerXform = Transform(asker);
            var lost = _container.IsEntityInContainer(uid)
                       || xform.GridUid == null
                       || xform.GridUid != askerXform.GridUid
                       || xform.MapID != askerXform.MapID
                       || (_transform.GetWorldPosition(xform) - _transform.GetWorldPosition(askerXform)).Length() > LostRange;

            if (!lost)
                continue;

            _container.TryRemoveFromContainer(uid, force: true);
            _transform.DropNextTo(uid, asker);
            _popup.PopupEntity(Loc.GetString("wg-mentor-critter-back"), asker, asker);
        }
    }
}
